namespace Vidora.Core.Services;

public interface IActiveDownloadCoordinator
{
    bool HasActiveDownload { get; }

    Guid? ActiveTaskId { get; }

    CancellationToken Begin(Guid taskId, CancellationToken cancellationToken);

    void Complete(Guid taskId);

    Task<bool> CancelAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
