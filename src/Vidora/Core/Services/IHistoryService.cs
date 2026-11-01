using Vidora.Core.Models;

namespace Vidora.Core.Services;

public interface IHistoryService
{
    Task<IReadOnlyList<DownloadHistoryItem>> LoadAsync(CancellationToken cancellationToken);

    Task AddAsync(DownloadHistoryItem item, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
