using Vidora.Core.Models;

namespace Vidora.Infrastructure.Persistence;

public sealed class QueueStore : IQueueStore
{
    private const int SchemaVersion = 1;
    private readonly AtomicJsonStore<QueueDocument> store;

    public QueueStore(string filePath, TimeProvider timeProvider)
    {
        store = new AtomicJsonStore<QueueDocument>(
            filePath,
            SchemaVersion,
            () => new QueueDocument(SchemaVersion, []),
            timeProvider);
    }

    public AtomicStoreLoadStatus LastLoadStatus { get; private set; } = AtomicStoreLoadStatus.Missing;

    public async Task<IReadOnlyList<DownloadQueueItem>> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await store.LoadAsync(cancellationToken);
        LastLoadStatus = result.Status;
        var restored = result.Document.Items
            .Select(item => item.Status == DownloadStatus.Downloading
                ? item with { Status = DownloadStatus.Interrupted, Progress = null }
                : item)
            .ToArray();
        if (result.Document.Items.Any(item => item.Status == DownloadStatus.Downloading))
        {
            await SaveAsync(restored, cancellationToken);
        }

        return restored;
    }

    public Task SaveAsync(IReadOnlyList<DownloadQueueItem> items, CancellationToken cancellationToken) =>
        store.SaveAsync(new QueueDocument(SchemaVersion, items.ToArray()), cancellationToken);
}
