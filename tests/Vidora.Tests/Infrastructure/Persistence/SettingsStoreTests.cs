using Vidora.Core.Models;
using Vidora.Infrastructure.Persistence;

namespace Vidora.Tests.Infrastructure.Persistence;

[TestClass]
public sealed class SettingsStoreTests
{
    [TestMethod]
    public async Task LoadAsync_LegacySettingsAndExternalFfmpeg_RetainCompatibleSchema()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"settings\":{\"lastDestinationDirectory\":\"C:\\\\Videos\",\"theme\":\"Dark\",\"notificationsEnabled\":false}}");
        var store = new SettingsStore(path, TimeProvider.System);
        var legacy = await store.LoadAsync(CancellationToken.None);
        Assert.IsNull(legacy.FfmpegPath);
        Assert.AreEqual("Dark", legacy.Theme);
        var updated = legacy with { FfmpegPath = @"C:\Program Files\FFmpeg\bin\ffmpeg.exe" };
        await store.SaveAsync(updated, CancellationToken.None);
        Assert.AreEqual(updated, await store.LoadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task UpdateAsync_ConcurrentFfmpegAndDestinationChanges_PreservesBothFields()
    {
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(Path.Combine(directory.Path, "settings.json"), TimeProvider.System);
        await store.SaveAsync(new AppSettings(@"C:\Old", "Dark", false), CancellationToken.None);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var ffmpeg = Task.Run(() => store.UpdateAsync(settings =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Update lock was not released.");
            return settings with { FfmpegPath = @"C:\FFmpeg\ffmpeg.exe" };
        }, CancellationToken.None));
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
        var destination = store.UpdateAsync(settings => settings with { LastDestinationDirectory = @"C:\New" }, CancellationToken.None);
        try { Assert.IsFalse(destination.IsCompleted); }
        finally { release.Set(); }
        await Task.WhenAll(ffmpeg, destination);
        var restored = await store.LoadAsync(CancellationToken.None);
        Assert.AreEqual(@"C:\FFmpeg\ffmpeg.exe", restored.FfmpegPath);
        Assert.AreEqual(@"C:\New", restored.LastDestinationDirectory);
        Assert.AreEqual("Dark", restored.Theme);
        Assert.IsFalse(restored.NotificationsEnabled);
    }

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
    public async Task SaveAndLoadAsync_ThemeAndNotifications_RoundTrip()
    {
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(Path.Combine(directory.Path, "settings.json"), TimeProvider.System);

        await store.SaveAsync(new AppSettings(@"C:\Videos", "Dark", false), CancellationToken.None);

        var restored = await store.LoadAsync(CancellationToken.None);
        Assert.AreEqual("Dark", restored.Theme);
        Assert.IsFalse(restored.NotificationsEnabled);
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
