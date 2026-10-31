using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Persistence;

namespace Vidora.Infrastructure.Processes;

public sealed class DownloadQueueService(
    IDownloadService downloadService,
    QueueStore queueStore,
    IActiveDownloadCoordinator activeDownloads) : IDownloadQueueService
{
    private readonly object sync = new();
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly List<DownloadQueueItem> items = [];
    private Task? processor;
    private CancellationTokenSource? activeCancellation;
    private bool initialized;
    private bool paused;

    public IReadOnlyList<DownloadQueueItem> Items
    {
        get
        {
            lock (sync)
            {
                return items.ToArray();
            }
        }
    }

    public event EventHandler? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var restored = await queueStore.LoadAsync(cancellationToken);
            lock (sync)
            {
                if (initialized)
                {
                    return;
                }

                items.AddRange(restored);
                paused = items.Count > 0;
                initialized = true;
            }
        }
        finally
        {
            mutationGate.Release();
        }

        RaiseChanged();
    }

    public async Task EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            DownloadQueueItem queued;
            lock (sync)
            {
                EnsureInitialized();
                var snapshot = Snapshot(request);
                queued = new DownloadQueueItem(
                    snapshot,
                    DownloadStatus.Pending,
                    null,
                    null,
                    null,
                    0,
                    items.Any(item => item.Request.SourceUrl == snapshot.SourceUrl));
                items.Add(queued);
            }

            await SaveCurrentAsync(cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }

        RaiseChanged();
        StartProcessorIfNeeded();
    }

    public Task ResumeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            EnsureInitialized();
            paused = false;
        }

        StartProcessorIfNeeded();
        return Task.CompletedTask;
    }

    public async Task CancelActiveAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cancellation;
        Task? running;
        lock (sync)
        {
            cancellation = activeCancellation;
            running = processor;
        }

        cancellation?.Cancel();
        if (cancellation is not null && running is not null)
        {
            await running.WaitAsync(cancellationToken);
        }
    }

    public async Task<bool> RemovePendingAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            lock (sync)
            {
                var index = items.FindIndex(item =>
                    item.Request.Id == taskId && item.Status == DownloadStatus.Pending);
                if (index < 0)
                {
                    return false;
                }

                items.RemoveAt(index);
            }

            await SaveCurrentAsync(cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }

        RaiseChanged();
        return true;
    }

    public async Task PersistInterruptedAsync(
        Guid taskId,
        DownloadResult result,
        CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            lock (sync)
            {
                var index = items.FindIndex(item => item.Request.Id == taskId);
                if (index < 0 || items[index].Status == DownloadStatus.Completed)
                {
                    return;
                }

                items[index] = items[index] with
                {
                    Status = DownloadStatus.Interrupted,
                    Progress = null,
                    FinalPath = result.FinalPath,
                    Error = result.Error
                };
                paused = true;
            }

            await SaveCurrentAsync(cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }

        RaiseChanged();
    }

    private void StartProcessorIfNeeded()
    {
        lock (sync)
        {
            if (!initialized || paused || !items.Any(item => item.Status == DownloadStatus.Pending) ||
                processor is { IsCompleted: false })
            {
                return;
            }

            processor = ProcessAsync();
        }
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            DownloadQueueItem active;
            CancellationTokenSource cancellation;
            await mutationGate.WaitAsync();
            try
            {
                lock (sync)
                {
                    var index = paused
                        ? -1
                        : items.FindIndex(item => item.Status == DownloadStatus.Pending);
                    if (index < 0)
                    {
                        processor = null;
                        return;
                    }

                    active = items[index] with
                    {
                        Status = DownloadStatus.Downloading,
                        Progress = null,
                        FinalPath = null,
                        Error = null,
                        AttemptCount = items[index].AttemptCount + 1
                    };
                    items[index] = active;
                    cancellation = new CancellationTokenSource();
                    activeCancellation = cancellation;
                }

                await SaveCurrentAsync(CancellationToken.None);
            }
            finally
            {
                mutationGate.Release();
            }

            RaiseChanged();
            var activeToken = activeDownloads.Begin(active.Request.Id, cancellation.Token);
            DownloadResult result;
            try
            {
                var progress = new Progress<DownloadProgress>(value => UpdateProgress(active.Request.Id, value));
                result = await downloadService.DownloadAsync(active.Request, progress, activeToken);
            }
            catch (OperationCanceledException) when (activeToken.IsCancellationRequested)
            {
                result = new DownloadResult(DownloadStatus.Cancelled, null, null);
            }
            catch (VidoraException exception)
            {
                result = new DownloadResult(DownloadStatus.Failed, null, exception.Error);
            }

            await mutationGate.WaitAsync();
            try
            {
                lock (sync)
                {
                    var index = items.FindIndex(item => item.Request.Id == active.Request.Id);
                    if (index >= 0)
                    {
                        items[index] = items[index] with
                        {
                            Status = result.Status,
                            Progress = null,
                            FinalPath = result.FinalPath,
                            Error = result.Error
                        };
                    }

                    if (result.Status is DownloadStatus.Cancelled or DownloadStatus.Failed or DownloadStatus.Interrupted)
                    {
                        paused = true;
                    }

                    activeCancellation = null;
                }

                await SaveCurrentAsync(CancellationToken.None);
            }
            finally
            {
                mutationGate.Release();
                activeDownloads.Complete(active.Request.Id);
                cancellation.Dispose();
            }

            RaiseChanged();
        }
    }

    private void UpdateProgress(Guid taskId, DownloadProgress progress)
    {
        lock (sync)
        {
            var index = items.FindIndex(item =>
                item.Request.Id == taskId && item.Status == DownloadStatus.Downloading);
            if (index < 0)
            {
                return;
            }

            items[index] = items[index] with { Progress = progress };
        }

        RaiseChanged();
    }

    private async Task SaveCurrentAsync(CancellationToken cancellationToken)
    {
        DownloadQueueItem[] snapshot;
        lock (sync)
        {
            snapshot = items.ToArray();
        }

        await queueStore.SaveAsync(snapshot, cancellationToken);
    }

    private static DownloadRequest Snapshot(DownloadRequest request) => request with
    {
        Metadata = request.Metadata with { Formats = request.Metadata.Formats.ToArray() }
    };

    private void EnsureInitialized()
    {
        if (!initialized)
        {
            throw new InvalidOperationException("The download queue has not been initialized.");
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
