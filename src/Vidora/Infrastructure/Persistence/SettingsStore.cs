using Vidora.Core.Models;
using Vidora.Core.Services;

namespace Vidora.Infrastructure.Persistence;

public sealed class SettingsStore : ISettingsService
{
    private const int SchemaVersion = 1;
    private readonly AtomicJsonStore<SettingsDocument> store;

    public SettingsStore(string filePath, TimeProvider timeProvider)
    {
        store = new AtomicJsonStore<SettingsDocument>(
            filePath,
            SchemaVersion,
            () => new SettingsDocument(SchemaVersion, new AppSettings(null)),
            timeProvider);
    }

    public AtomicStoreLoadStatus LastLoadStatus { get; private set; } = AtomicStoreLoadStatus.Missing;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await store.LoadAsync(cancellationToken);
        LastLoadStatus = result.Status;
        return result.Document.Settings;
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) =>
        store.SaveAsync(new SettingsDocument(SchemaVersion, settings), cancellationToken);
}
