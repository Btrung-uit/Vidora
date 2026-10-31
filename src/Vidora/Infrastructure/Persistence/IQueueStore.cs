using Vidora.Core.Models;

namespace Vidora.Infrastructure.Persistence;

public interface IQueueStore
{
    Task<IReadOnlyList<DownloadQueueItem>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyList<DownloadQueueItem> items, CancellationToken cancellationToken);
}
