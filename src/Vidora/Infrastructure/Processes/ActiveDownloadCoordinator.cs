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
        lock (sync)
        {
            if (activeDownload?.TaskId == taskId)
            {
                completed = activeDownload;
                activeDownload = null;
            }
        }

        if (completed is not null)
        {
            completed.Completion.TrySetResult();
            completed.Cancellation.Dispose();
        }
    }

    public async Task<bool> CancelAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ActiveDownload? active;
        lock (sync)
        {
            active = activeDownload;
        }

        if (active is null)
        {
            return true;
        }

        active.Cancellation.Cancel();
        try
        {
            await active.Completion.Task.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private sealed record ActiveDownload(
        Guid TaskId,
        CancellationTokenSource Cancellation,
        TaskCompletionSource Completion);
}
