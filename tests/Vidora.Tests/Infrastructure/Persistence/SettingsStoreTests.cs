using Vidora.Core.Models;
using Vidora.Infrastructure.Persistence;

namespace Vidora.Tests.Infrastructure.Persistence;

[TestClass]
public sealed class SettingsStoreTests
{
    [TestMethod]
    public async Task SaveAndLoadAsync_LastDestination_RoundTrips()
    {
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(Path.Combine(directory.Path, "settings.json"), TimeProvider.System);

        await store.SaveAsync(new AppSettings(@"C:\Videos"), CancellationToken.None);
        var restored = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(@"C:\Videos", restored.LastDestinationDirectory);
    }

    [TestMethod]
    public async Task LoadAsync_NullSettings_QuarantinesAndReturnsDefaults()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(
            path,
            "{\"schemaVersion\":1,\"settings\":null}",
            CancellationToken.None);
        var store = new SettingsStore(path, TimeProvider.System);

        var restored = await store.LoadAsync(CancellationToken.None);

        Assert.IsNull(restored.LastDestinationDirectory);
        Assert.AreEqual(AtomicStoreLoadStatus.RecoveredCorrupt, store.LastLoadStatus);
        Assert.IsFalse(File.Exists(path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-settings-store-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
