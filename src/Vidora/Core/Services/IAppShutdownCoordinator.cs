namespace Vidora.Core.Services;

public enum AppShutdownDecision
{
    ContinueDownloading,
    CancelDownloadAndExit
}

public enum AppShutdownOutcome
{
    ContinueRunning,
    ExitCompleted
}

public interface IAppShutdownCoordinator
{
    Task<AppShutdownOutcome> RequestShutdownAsync(CancellationToken cancellationToken);
}
