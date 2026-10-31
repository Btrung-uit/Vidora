using Vidora.Core.Models;

namespace Vidora.Core.Services;

public interface IDownloadQueueService
{
    IReadOnlyList<DownloadQueueItem> Items { get; }

    event EventHandler? Changed;

    Task InitializeAsync(CancellationToken cancellationToken);

    Task EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken);

    Task ResumeAsync(CancellationToken cancellationToken);

    Task CancelActiveAsync(CancellationToken cancellationToken);

    Task<bool> RemovePendingAsync(Guid taskId, CancellationToken cancellationToken);

    Task PersistInterruptedAsync(
        Guid taskId,
        DownloadResult result,
        CancellationToken cancellationToken);
}
