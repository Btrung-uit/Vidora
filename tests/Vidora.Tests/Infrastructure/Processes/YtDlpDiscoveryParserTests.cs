using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Dependencies;
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
            new AppToolPaths("C:\\tools\\yt-dlp.exe", "C:\\tools\\ffmpeg.exe"));

        var metadata = await service.DiscoverAsync("https://youtu.be/abc123", CancellationToken.None);

        Assert.AreEqual("abc123", metadata.MediaId);
        Assert.IsNotNull(capturedSpec);
        CollectionAssert.AreEqual(
            new[] { "--dump-single-json", "--skip-download", "--no-playlist", "--", "https://youtu.be/abc123" },
            capturedSpec.Arguments.ToArray());
    }

    [TestMethod]
    public async Task DiscoverAsync_DependencyNotReady_DoesNotStartProcess()
    {
        var runner = new FakeProcessRunner((_, _) =>
            throw new AssertFailedException("The process must not start when dependencies are not ready."));
        var service = new VideoDiscoveryService(
            runner,
            new FakeDependencyService(MissingReport()),
            new AppToolPaths("missing-yt-dlp.exe", "missing-ffmpeg.exe"));

        var exception = await Assert.ThrowsAsync<VidoraException>(
            () => service.DiscoverAsync("https://youtu.be/abc123", CancellationToken.None));

        Assert.AreEqual(VidoraErrorCode.DependencyMissing, exception.Error.Code);
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
    }

    private sealed class FakeProcessRunner(
        Func<ProcessSpec, CancellationToken, Task<ProcessResult>> run) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessSpec spec,
            IProgress<ProcessOutputLine>? output,
            CancellationToken cancellationToken) => run(spec, cancellationToken);
    }
}
