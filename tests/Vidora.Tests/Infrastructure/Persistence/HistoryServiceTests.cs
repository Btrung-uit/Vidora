using System.Text.Json;
using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Infrastructure.Persistence;

namespace Vidora.Tests.Infrastructure.Persistence;

[TestClass]
public sealed class HistoryServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AddAsync_TerminalItems_PreservesAttemptSnapshots()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path);
        var expected = new[]
        {
            Item(1, DownloadStatus.Completed, @"C:\Videos\one.mp4", null),
            Item(2, DownloadStatus.Failed, null, VidoraErrorCode.NetworkFailure),
            Item(3, DownloadStatus.Cancelled, null, VidoraErrorCode.Cancelled),
            Item(4, DownloadStatus.Interrupted, @"C:\Videos\partial.webm", VidoraErrorCode.Unknown)
        };

        foreach (var item in expected)
        {
            await service.AddAsync(item, CancellationToken.None);
        }

        var actual = await service.LoadAsync(CancellationToken.None);

        var orderedExpected = expected.Reverse().ToArray();
        Assert.HasCount(orderedExpected.Length, actual);
        for (var index = 0; index < orderedExpected.Length; index++)
        {
            var expectedItem = orderedExpected[index];
            var actualItem = actual[index];
            Assert.AreEqual(expectedItem.TaskId, actualItem.TaskId);
            Assert.AreEqual(expectedItem.AttemptCount, actualItem.AttemptCount);
            Assert.AreEqual(expectedItem.Timestamp, actualItem.Timestamp);
            Assert.AreEqual(expectedItem.SourceUrl, actualItem.SourceUrl);
            Assert.AreEqual(expectedItem.MediaId, actualItem.MediaId);
            Assert.AreEqual(expectedItem.Title, actualItem.Title);
            Assert.AreEqual(expectedItem.Platform, actualItem.Platform);
            CollectionAssert.AreEqual(expectedItem.Formats.ToArray(), actualItem.Formats.ToArray());
            Assert.AreEqual(expectedItem.Quality, actualItem.Quality);
            Assert.AreEqual(expectedItem.DestinationDirectory, actualItem.DestinationDirectory);
            Assert.AreEqual(expectedItem.Status, actualItem.Status);
            Assert.AreEqual(expectedItem.FinalPath, actualItem.FinalPath);
            Assert.AreEqual(expectedItem.ErrorCode, actualItem.ErrorCode);
        }
    }

    [TestMethod]
    public async Task AddAsync_MoreThanOneThousandItems_KeepsNewestOneThousand()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "history.json");
        var existing = Enumerable.Range(0, 1_000).Select(index => Item(index, DownloadStatus.Completed)).ToArray();
        var json = JsonSerializer.Serialize(
            new { schemaVersion = 1, items = existing },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await File.WriteAllTextAsync(path, json, CancellationToken.None);
        var service = new HistoryService(path, TimeProvider.System);
        var newest = Item(1_000, DownloadStatus.Completed);

        await service.AddAsync(newest, CancellationToken.None);

        var actual = await service.LoadAsync(CancellationToken.None);
        Assert.HasCount(1_000, actual);
        Assert.AreEqual(newest.TaskId, actual[0].TaskId);
        Assert.IsFalse(actual.Any(item => item.TaskId == existing[0].TaskId));
    }

    [TestMethod]
    public async Task LoadAsync_MalformedJson_QuarantinesAndReturnsEmptyHistory()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "history.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,", CancellationToken.None);
        var service = new HistoryService(path, TimeProvider.System);

        var actual = await service.LoadAsync(CancellationToken.None);

        Assert.IsEmpty(actual);
        Assert.AreEqual(AtomicStoreLoadStatus.RecoveredCorrupt, service.LastLoadStatus);
        Assert.IsFalse(File.Exists(path));
        Assert.HasCount(1, Directory.GetFiles(directory.Path, "history.json.corrupt-*"));
    }

    [TestMethod]
    public async Task LoadAsync_NullFormats_QuarantinesAndReturnsEmptyHistory()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "history.json");
        const string json = """
            {"schemaVersion":1,"items":[{"taskId":"11111111-1111-1111-1111-111111111111","attemptCount":1,"timestamp":"2026-11-01T08:00:00Z","sourceUrl":"https://youtu.be/abc123","mediaId":"abc123","title":"Video","platform":1,"formats":null,"quality":2,"destinationDirectory":"C:\\Videos","status":2,"finalPath":null,"errorCode":null}]}
            """;
        await File.WriteAllTextAsync(path, json, CancellationToken.None);
        var service = new HistoryService(path, TimeProvider.System);

        var actual = await service.LoadAsync(CancellationToken.None);

        Assert.IsEmpty(actual);
        Assert.AreEqual(AtomicStoreLoadStatus.RecoveredCorrupt, service.LastLoadStatus);
        Assert.HasCount(1, Directory.GetFiles(directory.Path, "history.json.corrupt-*"));
    }

    [TestMethod]
    public async Task AddAsync_SameAttemptAgain_ReplacesEarlierTerminalSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path);
        var cancelled = Item(1, DownloadStatus.Cancelled, errorCode: VidoraErrorCode.Cancelled);
        var interrupted = cancelled with
        {
            Timestamp = cancelled.Timestamp.AddSeconds(1),
            Status = DownloadStatus.Interrupted,
            ErrorCode = null
        };

        await service.AddAsync(cancelled, CancellationToken.None);
        await service.AddAsync(interrupted, CancellationToken.None);

        var actual = await service.LoadAsync(CancellationToken.None);
        Assert.HasCount(1, actual);
        Assert.AreEqual(DownloadStatus.Interrupted, actual[0].Status);
        Assert.AreEqual(interrupted.Timestamp, actual[0].Timestamp);
    }

    [TestMethod]
    public async Task ClearAsync_ExistingMedia_RemovesOnlyHistoryRecords()
    {
        using var directory = new TemporaryDirectory();
        var mediaPath = Path.Combine(directory.Path, "keep.mp4");
        await File.WriteAllTextAsync(mediaPath, "media", CancellationToken.None);
        var service = CreateService(directory.Path);
        await service.AddAsync(Item(1, DownloadStatus.Completed, mediaPath), CancellationToken.None);

        await service.ClearAsync(CancellationToken.None);

        Assert.IsEmpty(await service.LoadAsync(CancellationToken.None));
        Assert.IsTrue(File.Exists(mediaPath));
        Assert.AreEqual("media", await File.ReadAllTextAsync(mediaPath, CancellationToken.None));
    }

    private static HistoryService CreateService(string directory) =>
        new(Path.Combine(directory, "history.json"), TimeProvider.System);

    private static DownloadHistoryItem Item(
        int index,
        DownloadStatus status,
        string? finalPath = null,
        VidoraErrorCode? errorCode = null)
    {
        var taskId = new Guid(index + 1, 0, 0, new byte[8]);
        return new DownloadHistoryItem(
            taskId,
            index + 1,
            Start.AddMinutes(index),
            new Uri($"https://youtu.be/video{index}"),
            $"video{index}",
            $"Video {index}",
            index % 2 == 0 ? VideoPlatform.YouTube : VideoPlatform.TikTok,
            [new VideoFormat("18", "mp4", 1280, 720, "avc1", "mp4a", 1_000)],
            index % 2 == 0 ? QualityPreset.P720 : QualityPreset.Best,
            $@"C:\Videos\{index}",
            status,
            finalPath,
            errorCode);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-history-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
