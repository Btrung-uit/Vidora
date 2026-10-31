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

        while ((taskId = activeDownloads.ActiveTaskId) is not null)
        {
            if (!await activeDownloads.CancelAndWaitAsync(taskId.Value, shutdownTimeout, cancellationToken))
            {
                continue;
            }

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

            break;
        }

        return AppShutdownOutcome.ExitCompleted;
    }
}
