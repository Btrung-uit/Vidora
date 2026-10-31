using Vidora.Core.Models;
using Vidora.Infrastructure.Persistence;

namespace Vidora.Tests.Infrastructure.Persistence;

[TestClass]
public sealed class QueueStoreTests
{
    [TestMethod]
    public async Task LoadAsync_DownloadingItem_BecomesInterruptedWhilePendingRemainsPending()
    {
        using var directory = new TemporaryDirectory();
        var store = new QueueStore(Path.Combine(directory.Path, "queue.json"), TimeProvider.System);
        var pending = Item(Guid.Parse("11111111-1111-1111-1111-111111111111"), DownloadStatus.Pending);
        var downloading = Item(Guid.Parse("22222222-2222-2222-2222-222222222222"), DownloadStatus.Downloading);
        await store.SaveAsync([pending, downloading], CancellationToken.None);

        var restored = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(2, restored.Count);
        Assert.AreEqual(DownloadStatus.Pending, restored[0].Status);
        Assert.AreEqual(DownloadStatus.Interrupted, restored[1].Status);
    }

    private static DownloadQueueItem Item(Guid id, DownloadStatus status) => new(
        Request(id),
        status,
        null,
        null,
        null,
        0,
        false);

    private static DownloadRequest Request(Guid id) => new(
        id,
        new Uri($"https://youtu.be/{id:N}"),
        new VideoMetadata(
            new Uri($"https://youtu.be/{id:N}"),
            VideoPlatform.YouTube,
            id.ToString("N"),
            "Video",
            "Author",
            TimeSpan.FromMinutes(1),
            null,
            [new VideoFormat("18", "mp4", 1280, 720, "avc1", "mp4a", 1_000)]),
        QualityPreset.P720,
        @"C:\Videos",
        new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-queue-store-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
