using Vidora.Core.Services;

namespace Vidora.Infrastructure.Processes;

public sealed class ActiveDownloadCoordinator : IActiveDownloadCoordinator
{
    private readonly Lock sync = new();
    private ActiveDownload? activeDownload;

    public bool HasActiveDownload
    {
        get
        {
            lock (sync)
            {
                return activeDownload is not null;
            }
        }
    }

    public Guid? ActiveTaskId
    {
        get
        {
            lock (sync)
            {
                return activeDownload?.TaskId;
            }
        }
    }

    public CancellationToken Begin(Guid taskId, CancellationToken cancellationToken)
    {
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (sync)
        {
            if (activeDownload is not null)
            {
                linkedCancellation.Dispose();
                throw new InvalidOperationException("Only one download can be active.");
            }

            activeDownload = new ActiveDownload(
                taskId,
                linkedCancellation,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            return linkedCancellation.Token;
        }
    }

    public void Complete(Guid taskId)
    {
        ActiveDownload? completed = null;
        CancellationTokenSource? cancellationToDispose = null;
        lock (sync)
        {
            if (activeDownload?.TaskId == taskId)
            {
                completed = activeDownload;
                activeDownload = null;
                completed.Completion.TrySetResult();
                if (!completed.ShutdownRequested || completed.ShutdownCallFinished)
                {
                    completed.Disposed = true;
                    cancellationToDispose = completed.Cancellation;
                }
            }
        }

        cancellationToDispose?.Dispose();
    }

    public async Task<bool> CancelAndWaitAsync(
        Guid taskId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ActiveDownload? active;
        lock (sync)
        {
            active = activeDownload;
            if (active?.TaskId != taskId)
            {
                return false;
            }

            active.ShutdownRequested = true;
        }

        try
        {
            active.Cancellation.Cancel();
            try
            {
                await active.Completion.Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
            }

            return true;
        }
        finally
        {
            CancellationTokenSource? cancellationToDispose = null;
            lock (sync)
            {
                active.ShutdownCallFinished = true;
                if (active.Completion.Task.IsCompleted && !active.Disposed)
                {
                    active.Disposed = true;
                    cancellationToDispose = active.Cancellation;
                }
            }

            cancellationToDispose?.Dispose();
        }
    }

    private sealed class ActiveDownload(
        Guid taskId,
        CancellationTokenSource cancellation,
        TaskCompletionSource completion)
    {
        public Guid TaskId { get; } = taskId;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public TaskCompletionSource Completion { get; } = completion;

        public bool ShutdownRequested { get; set; }

        public bool ShutdownCallFinished { get; set; }

        public bool Disposed { get; set; }
    }
}
