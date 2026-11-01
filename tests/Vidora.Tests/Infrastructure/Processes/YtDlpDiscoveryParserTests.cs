using System.ComponentModel;
using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Logging;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class YtDlpDiscoveryParserTests
{
    [TestMethod]
    public void Parse_CompleteJson_MapsPreviewMetadata()
    {
        var json = ReadFixture("youtube-complete.json");

        var metadata = YtDlpDiscoveryParser.Parse(json, "https://youtu.be/abc123");

        Assert.AreEqual(new Uri("https://youtu.be/abc123"), metadata.SourceUrl);
        Assert.AreEqual(VideoPlatform.YouTube, metadata.Platform);
        Assert.AreEqual("abc123", metadata.MediaId);
        Assert.AreEqual("A complete video", metadata.Title);
        Assert.AreEqual("Vidora Fixtures", metadata.Author);
        Assert.AreEqual(TimeSpan.FromSeconds(125.5), metadata.Duration);
        Assert.AreEqual(new Uri("https://example.test/thumb.jpg"), metadata.ThumbnailUrl);
        Assert.HasCount(2, metadata.Formats);
        Assert.AreEqual(new VideoFormat("137", "mp4", 1920, 1080, "avc1.640028", "none", 10_000_000), metadata.Formats[0]);
    }

    [TestMethod]
    public void Parse_MissingOptionalFields_UsesNullsAndEmptyFormats()
    {
        var json = ReadFixture("missing-optional-fields.json");

        var metadata = YtDlpDiscoveryParser.Parse(json, "https://vm.tiktok.com/xyz/");

        Assert.AreEqual(VideoPlatform.TikTok, metadata.Platform);
        Assert.IsNull(metadata.Author);
        Assert.IsNull(metadata.Duration);
        Assert.IsNull(metadata.ThumbnailUrl);
        Assert.IsEmpty(metadata.Formats);
    }

    [TestMethod]
    public void Parse_MalformedJson_ThrowsStableDiscoveryError()
    {
        var exception = Assert.Throws<VidoraException>(
            () => YtDlpDiscoveryParser.Parse("{not-json", "https://youtu.be/abc123"));

        Assert.AreEqual(VidoraErrorCode.Unknown, exception.Error.Code);
        Assert.AreEqual("Error.DiscoveryFailed", exception.Error.UserMessageKey);
        Assert.AreEqual("Action.TryAgain", exception.Error.SuggestedActionKey);
    }

    [TestMethod]
    [DataRow("1e309")]
    [DataRow("-1")]
    public void Parse_InvalidDuration_TreatsDurationAsUnavailable(string duration)
    {
        var json = $$"""
            { "id": "abc123", "title": "Video", "extractor_key": "Youtube", "duration": {{duration}} }
            """;

        var metadata = YtDlpDiscoveryParser.Parse(json, "https://youtu.be/abc123");

        Assert.IsNull(metadata.Duration);
    }

    [TestMethod]
    [DataRow("{ \"id\": \"live\", \"title\": \"Live\", \"extractor_key\": \"Youtube\", \"is_live\": true }")]
    [DataRow("{ \"id\": \"upcoming\", \"title\": \"Upcoming\", \"extractor_key\": \"Youtube\", \"live_status\": \"is_upcoming\" }")]
    [DataRow("{ \"id\": \"collection\", \"title\": \"Collection\", \"extractor_key\": \"Youtube\", \"_type\": \"playlist\", \"entries\": [] }")]
    public void Parse_LiveOrCollectionMetadata_ThrowsStableUnsupportedContentError(string json)
    {
        var exception = Assert.Throws<VidoraException>(
            () => YtDlpDiscoveryParser.Parse(json, "https://youtube.com/watch?v=abc123"));

        Assert.AreEqual(VidoraErrorCode.Unavailable, exception.Error.Code);
        Assert.AreEqual("Error.UnsupportedContent", exception.Error.UserMessageKey);
    }

    [TestMethod]
    [DataRow("file:///C:/Users/example/thumb.jpg")]
    [DataRow("file://remotehost/share/thumb.jpg")]
    [DataRow("ftp://example.test/thumb.jpg")]
    public void Parse_NonWebThumbnail_DoesNotExposeUriToPreview(string thumbnail)
    {
        var json = $$"""
            { "id": "abc123", "title": "Video", "extractor_key": "Youtube", "thumbnail": "{{thumbnail}}" }
            """;

        var metadata = YtDlpDiscoveryParser.Parse(json, "https://youtu.be/abc123");

        Assert.IsNull(metadata.ThumbnailUrl);
    }

    [TestMethod]
    public async Task DiscoverAsync_ReadyDependencies_RunsExactCommandAndParsesOutput()
    {
        ProcessSpec? capturedSpec = null;
        var runner = new FakeProcessRunner((spec, _) =>
        {
            capturedSpec = spec;
            return Task.FromResult(new ProcessResult(0, [ReadFixture("youtube-complete.json")], []));
        });
        var service = new VideoDiscoveryService(
            runner,
            new FakeDependencyService(ReadyReport()),
            new AppToolPaths("C:\\tools\\yt-dlp.exe", "C:\\tools\\ffmpeg.exe"),
            new CapturedLogger());

        var metadata = await service.DiscoverAsync("https://youtu.be/abc123", CancellationToken.None);

        Assert.AreEqual("abc123", metadata.MediaId);
        Assert.IsNotNull(capturedSpec);
        CollectionAssert.AreEqual(
            new[] { "--ignore-config", "--dump-single-json", "--skip-download", "--no-playlist", "--", "https://youtu.be/abc123" },
            capturedSpec.Arguments.ToArray());
    }

    [TestMethod]
    public async Task DiscoverAsync_ProcessStartFailure_ThrowsStableDependencyError()
    {
        var runner = new FakeProcessRunner((_, _) =>
            Task.FromException<ProcessResult>(new Win32Exception("Executable disappeared.")));
        var service = new VideoDiscoveryService(
            runner,
            new FakeDependencyService(ReadyReport()),
            new AppToolPaths("missing-yt-dlp.exe", "C:\\tools\\ffmpeg.exe"),
            new CapturedLogger());

        var exception = await Assert.ThrowsAsync<VidoraException>(
            () => service.DiscoverAsync("https://youtu.be/abc123", CancellationToken.None));

        Assert.AreEqual(VidoraErrorCode.DependencyInvalid, exception.Error.Code);
        Assert.AreEqual("Error.DependencyInvalid", exception.Error.UserMessageKey);
    }

    [TestMethod]
    public async Task DiscoverAsync_DependencyNotReady_DoesNotStartProcess()
    {
        var runner = new FakeProcessRunner((_, _) =>
            throw new AssertFailedException("The process must not start when dependencies are not ready."));
        var service = new VideoDiscoveryService(
            runner,
            new FakeDependencyService(MissingReport()),
            new AppToolPaths("missing-yt-dlp.exe", "missing-ffmpeg.exe"),
            new CapturedLogger());

        var exception = await Assert.ThrowsAsync<VidoraException>(
            () => service.DiscoverAsync("https://youtu.be/abc123", CancellationToken.None));

        Assert.AreEqual(VidoraErrorCode.DependencyMissing, exception.Error.Code);
    }

    [TestMethod]
    public async Task DiscoverAsync_InvalidUrl_LogsStableValidationFailure()
    {
        var logger = new CapturedLogger();
        var runner = new FakeProcessRunner((_, _) =>
            throw new AssertFailedException("Invalid URL must not start the process."));
        var service = new VideoDiscoveryService(
            runner,
            new FakeDependencyService(ReadyReport()),
            new AppToolPaths("yt-dlp.exe", "ffmpeg.exe"),
            logger);

        var exception = await Assert.ThrowsAsync<VidoraException>(
            () => service.DiscoverAsync("not-a-url", CancellationToken.None));

        Assert.AreEqual(VidoraErrorCode.InvalidUrl, exception.Error.Code);
        Assert.AreEqual(VidoraErrorCode.InvalidUrl, logger.Entries.Single().ErrorCode);
    }

    [TestMethod]
    public async Task DiscoverAsync_NonZeroExit_ClassifiesAndLogsRedactedFailure()
    {
        var logger = new CapturedLogger();
        var runner = new FakeProcessRunner((_, _) => Task.FromResult(new ProcessResult(
            1,
            ["raw metadata must not be logged"],
            ["ERROR: Unable to download webpage: https://example.test/?token=secret-value"])));
        var service = new VideoDiscoveryService(
            runner,
            new FakeDependencyService(ReadyReport()),
            new AppToolPaths("C:\\tools\\yt-dlp.exe", "C:\\tools\\ffmpeg.exe"),
            logger);

        var exception = await Assert.ThrowsAsync<VidoraException>(
            () => service.DiscoverAsync("https://youtu.be/abc123", CancellationToken.None));

        Assert.AreEqual(VidoraErrorCode.NetworkFailure, exception.Error.Code);
        var entry = logger.Entries.Single();
        Assert.AreNotEqual(Guid.Empty, entry.OperationId);
        Assert.AreEqual(exception.Error.Code, entry.ErrorCode);
        StringAssert.Contains(entry.Detail, "token=[REDACTED]");
        Assert.IsFalse(entry.Detail?.Contains("secret-value", StringComparison.Ordinal) ?? false);
        Assert.IsFalse(entry.Detail?.Contains("raw metadata", StringComparison.Ordinal) ?? false);
    }

    [TestMethod]
    public async Task DiscoverAsync_LoggerFailure_DoesNotReplaceClassifiedError()
    {
        var runner = new FakeProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(1, [], ["ERROR: Video unavailable"])));
        var service = new VideoDiscoveryService(
            runner,
            new FakeDependencyService(ReadyReport()),
            new AppToolPaths("yt-dlp.exe", "ffmpeg.exe"),
            new ThrowingLogger());

        var exception = await Assert.ThrowsAsync<VidoraException>(
            () => service.DiscoverAsync("https://youtu.be/abc123", CancellationToken.None));

        Assert.AreEqual(VidoraErrorCode.Unavailable, exception.Error.Code);
    }

    [TestMethod]
    public async Task DiscoverAsync_CallerCancellation_RemainsCancellationAndIsLogged()
    {
        var logger = new CapturedLogger();
        var runner = new FakeProcessRunner(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new AssertFailedException("Cancellation should stop discovery.");
        });
        var service = new VideoDiscoveryService(
            runner,
            new FakeDependencyService(ReadyReport()),
            new AppToolPaths("yt-dlp.exe", "ffmpeg.exe"),
            logger);
        using var cancellation = new CancellationTokenSource();
        var discovery = service.DiscoverAsync("https://youtu.be/abc123", cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => discovery);
        Assert.AreEqual(VidoraErrorCode.Cancelled, logger.Entries.Single().ErrorCode);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Discovery", name));

    private static DependencyReport ReadyReport() => new(
        new DependencyComponentReport(DependencyStatus.Ready, "2026.10.30"),
        new DependencyComponentReport(DependencyStatus.Ready, "7.1.1"));

    private static DependencyReport MissingReport() => new(
        new DependencyComponentReport(DependencyStatus.Missing, null),
        new DependencyComponentReport(DependencyStatus.Ready, "7.1.1"));

    private sealed class FakeDependencyService(DependencyReport report) : IDependencyService
    {
        public Task<DependencyReport> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(report);

        public Task<string> GetLatestYtDlpVersionAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DependencyReport> UpdateYtDlpAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProcessRunner(
        Func<ProcessSpec, CancellationToken, Task<ProcessResult>> run) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessSpec spec,
            IProgress<ProcessOutputLine>? output,
            CancellationToken cancellationToken) => run(spec, cancellationToken);
    }

    private sealed class CapturedLogger : IAppLogger
    {
        public List<FileLogEntry> Entries { get; } = [];

        public Task WriteAsync(FileLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingLogger : IAppLogger
    {
        public Task WriteAsync(FileLogEntry entry, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("Log path unavailable."));
    }
}
