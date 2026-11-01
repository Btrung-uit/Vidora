using Vidora.Core.Models;
using Vidora.Core.Services;

namespace Vidora.Infrastructure.Persistence;

public sealed class HistoryService : IHistoryService
{
    private const int MaximumItems = 1_000;
    private const int SchemaVersion = 1;
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly AtomicJsonStore<HistoryDocument> store;

    public HistoryService(string filePath, TimeProvider timeProvider)
    {
        store = new AtomicJsonStore<HistoryDocument>(
            filePath,
            SchemaVersion,
            () => new HistoryDocument(SchemaVersion, []),
            timeProvider);
    }

    public AtomicStoreLoadStatus LastLoadStatus { get; private set; } = AtomicStoreLoadStatus.Missing;

    public async Task<IReadOnlyList<DownloadHistoryItem>> LoadAsync(CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var result = await store.LoadAsync(cancellationToken);
            LastLoadStatus = result.Status;
            return result.Document.Items
                .OrderByDescending(item => item.Timestamp)
                .ToArray();
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task AddAsync(DownloadHistoryItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var result = await store.LoadAsync(cancellationToken);
            LastLoadStatus = result.Status;
            var items = result.Document.Items
                .Where(existing => existing.TaskId != item.TaskId || existing.AttemptCount != item.AttemptCount)
                .Append(item)
                .OrderByDescending(existing => existing.Timestamp)
                .Take(MaximumItems)
                .ToArray();
            await store.SaveAsync(new HistoryDocument(SchemaVersion, items), cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            await store.SaveAsync(new HistoryDocument(SchemaVersion, []), cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }
    }
}
