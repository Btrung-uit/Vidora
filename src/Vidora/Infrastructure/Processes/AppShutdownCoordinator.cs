using Vidora.Core.Models;
using Vidora.Core.Services;

namespace Vidora.Infrastructure.Processes;

public sealed class AppShutdownCoordinator(
    IActiveDownloadCoordinator activeDownloads,
    Func<AppShutdownDecision> requestDecision,
    Func<Guid, DownloadResult, CancellationToken, Task> persistInterrupted,
    TimeSpan shutdownTimeout) : IAppShutdownCoordinator
{
    public async Task<AppShutdownOutcome> RequestShutdownAsync(CancellationToken cancellationToken)
    {
        var taskId = activeDownloads.ActiveTaskId;
        if (taskId is null)
        {
            return AppShutdownOutcome.ExitCompleted;
        }

        if (requestDecision() == AppShutdownDecision.ContinueDownloading)
        {
            return AppShutdownOutcome.ContinueRunning;
        }

        await activeDownloads.CancelAndWaitAsync(shutdownTimeout, cancellationToken);
        try
        {
            await persistInterrupted(
                    taskId.Value,
                    new DownloadResult(DownloadStatus.Interrupted, null, null),
                    cancellationToken)
                .WaitAsync(shutdownTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
        }

        return AppShutdownOutcome.ExitCompleted;
    }
}
