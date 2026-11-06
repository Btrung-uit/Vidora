using System.IO;
using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Persistence;

namespace Vidora.Infrastructure.Processes;

public sealed class DownloadQueueService(
    IDownloadService downloadService,
    IQueueStore queueStore,
    IActiveDownloadCoordinator activeDownloads,
    IHistoryService historyService,
    TimeProvider timeProvider) : IDownloadQueueService
{
    private readonly object sync = new();
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly List<DownloadQueueItem> items = [];
    private Task? processor;
    private CancellationTokenSource? activeCancellation;
    private Guid? currentTaskId;
    private bool initialized;
    private bool paused;
    private bool hasHistoryPersistenceError;

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

    public bool HasHistoryPersistenceError
    {
        get
        {
            lock (sync)
            {
                return hasHistoryPersistenceError;
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
                paused = items.Any(item => item.Status == DownloadStatus.Pending);
                initialized = true;
            }

            await ReconcileHistoryAsync(cancellationToken);
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
            var history = await historyService.LoadAsync(cancellationToken);
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
                    items.Any(item => item.Request.SourceUrl == snapshot.SourceUrl) ||
                    history.Any(item =>
                        item.Status == DownloadStatus.Completed && item.SourceUrl == snapshot.SourceUrl));
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

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (!await ReconcileHistoryAsync(cancellationToken))
            {
                return;
            }

            lock (sync)
            {
                EnsureInitialized();
                paused = false;
            }
        }
        finally
        {
            mutationGate.Release();
        }

        StartProcessorIfNeeded();
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

    public async Task<bool> RemoveAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            DownloadQueueItem? candidate;
            lock (sync)
            {
                candidate = items.FirstOrDefault(item =>
                    item.Request.Id == taskId &&
                    item.Status is DownloadStatus.Pending or DownloadStatus.Failed);
            }

            if (candidate is null)
            {
                return false;
            }

            if (candidate.Status == DownloadStatus.Failed && !candidate.IsHistoryRecorded &&
                !await TryRecordHistoryAsync(candidate, cancellationToken))
            {
                return false;
            }

            lock (sync)
            {
                items.RemoveAll(item => item.Request.Id == taskId);
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
        DownloadQueueItem? interrupted = null;
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
                    Error = result.Error,
                    IsHistoryRecorded = false
                };
                interrupted = items[index];
                paused = true;
            }

            await SaveCurrentAsync(cancellationToken);
            await TryRecordHistoryAsync(interrupted, cancellationToken);
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
        try
        {
            while (true)
            {
                DownloadQueueItem active;
                CancellationTokenSource cancellation;
                DownloadQueueItem? terminal = null;
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
                        currentTaskId = active.Request.Id;
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
                                Error = result.Error,
                                IsHistoryRecorded = false
                            };
                            terminal = items[index];
                        }

                        if (result.Status is DownloadStatus.Cancelled or DownloadStatus.Failed or DownloadStatus.Interrupted)
                        {
                            paused = true;
                        }
                    }

                    await SaveCurrentAsync(CancellationToken.None);
                    if (terminal is not null)
                    {
                        await TryRecordHistoryAsync(terminal, CancellationToken.None);
                    }

                    lock (sync)
                    {
                        activeCancellation = null;
                        currentTaskId = null;
                    }
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            HandlePersistenceFailure(exception);
        }
    }

    private void HandlePersistenceFailure(Exception exception)
    {
        CancellationTokenSource? cancellation;
        lock (sync)
        {
            var index = currentTaskId is { } taskId
                ? items.FindIndex(item => item.Request.Id == taskId)
                : -1;
            if (index >= 0)
            {
                if (items[index].Status == DownloadStatus.Downloading)
                {
                    items[index] = items[index] with
                    {
                        Status = DownloadStatus.Failed,
                        Progress = null,
                        Error = new VidoraError(
                            VidoraErrorCode.DiskFailure,
                            "Error.QueuePersistenceFailed",
                            "Action.TryAgain",
                            exception.Message,
                            true)
                    };
                }
            }

            paused = true;
            processor = null;
            cancellation = activeCancellation;
            activeCancellation = null;
            currentTaskId = null;
        }

        cancellation?.Dispose();
        RaiseChanged();
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

    private async Task<bool> ReconcileHistoryAsync(CancellationToken cancellationToken)
    {
        DownloadQueueItem[] unrecorded;
        lock (sync)
        {
            unrecorded = items
                .Where(item => IsTerminal(item.Status) && !item.IsHistoryRecorded)
                .ToArray();
        }

        foreach (var item in unrecorded)
        {
            if (!await TryRecordHistoryAsync(item, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> TryRecordHistoryAsync(
        DownloadQueueItem item,
        CancellationToken cancellationToken)
    {
        try
        {
            await historyService.AddAsync(ToHistoryItem(item), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lock (sync)
            {
                hasHistoryPersistenceError = true;
                paused = true;
            }

            return false;
        }

        lock (sync)
        {
            var index = items.FindIndex(existing =>
                existing.Request.Id == item.Request.Id && existing.AttemptCount == item.AttemptCount);
            if (index >= 0)
            {
                items[index] = items[index] with { IsHistoryRecorded = true };
            }

            hasHistoryPersistenceError = items.Any(existing =>
                IsTerminal(existing.Status) && !existing.IsHistoryRecorded);
        }

        await SaveCurrentAsync(cancellationToken);
        return true;
    }

    private static bool IsTerminal(DownloadStatus status) =>
        status is DownloadStatus.Completed or DownloadStatus.Failed or
            DownloadStatus.Cancelled or DownloadStatus.Interrupted;

    private static DownloadRequest Snapshot(DownloadRequest request) => request with
    {
        Metadata = request.Metadata with { Formats = request.Metadata.Formats.ToArray() }
    };

    private DownloadHistoryItem ToHistoryItem(DownloadQueueItem item) => new(
        item.Request.Id,
        item.AttemptCount,
        timeProvider.GetUtcNow(),
        item.Request.SourceUrl,
        item.Request.Metadata.MediaId,
        item.Request.Metadata.Title,
        item.Request.Metadata.Platform,
        item.Request.Metadata.Formats.ToArray(),
        item.Request.Quality,
        item.Request.DestinationDirectory,
        item.Status,
        item.FinalPath,
        item.Error?.Code);

    private void EnsureInitialized()
    {
        if (!initialized)
        {
            throw new InvalidOperationException("The download queue has not been initialized.");
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
