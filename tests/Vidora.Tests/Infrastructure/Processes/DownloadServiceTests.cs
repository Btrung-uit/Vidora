using System.ComponentModel;
using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Files;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class DownloadServiceTests
{
    [TestMethod]
    public async Task DownloadAsync_ProcessProgress_ForwardsParsedProgress()
    {
        using var directory = new TemporaryDirectory();
        var finalPath = Path.Combine(directory.Path, "video.mp4");
        var runner = new FakeProcessRunner((_, output, _) =>
        {
            output?.Report(Line("VIDORA_PROGRESS:25.5|1024|4096|512|6|downloading"));
            File.WriteAllBytes(finalPath, [1]);
            output?.Report(Line($"VIDORA_RESULT:{finalPath}"));
            return Task.FromResult(Result(0));
        });
        var progress = new CapturedProgress();

        await CreateService(runner, directory.Path).DownloadAsync(Request(directory.Path), progress, CancellationToken.None);

        Assert.HasCount(1, progress.Values);
        Assert.AreEqual(25.5, progress.Values[0].Percent);
        Assert.AreEqual(1024L, progress.Values[0].DownloadedBytes);
    }

    [TestMethod]
    public async Task DownloadAsync_NonZeroExit_ThrowsStableDownloadError()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(new FakeProcessRunner((_, _, _) => Task.FromResult(Result(1))), directory.Path);

        var exception = await Assert.ThrowsAsync<VidoraException>(
            () => service.DownloadAsync(Request(directory.Path), new CapturedProgress(), CancellationToken.None));

        Assert.AreEqual(VidoraErrorCode.Unknown, exception.Error.Code);
        Assert.AreEqual("Error.DownloadFailed", exception.Error.UserMessageKey);
    }

    [TestMethod]
    public async Task DownloadAsync_ProcessStartFailure_ThrowsStableDependencyError()
    {
        using var directory = new TemporaryDirectory();
        var runner = new FakeProcessRunner((_, _, _) =>
            Task.FromException<ProcessResult>(new Win32Exception("Executable unavailable.")));

        var exception = await Assert.ThrowsAsync<VidoraException>(
            () => CreateService(runner, directory.Path).DownloadAsync(Request(directory.Path), new CapturedProgress(), CancellationToken.None));

        Assert.AreEqual(VidoraErrorCode.DependencyInvalid, exception.Error.Code);
    }

    [TestMethod]
    public async Task DownloadAsync_SuccessWithoutExistingFinalPath_ThrowsPostProcessingError()
    {
        using var directory = new TemporaryDirectory();
        var missing = Path.Combine(directory.Path, "missing.mp4");
        var runner = new FakeProcessRunner((_, output, _) =>
        {
            output?.Report(Line($"VIDORA_RESULT:{missing}"));
            return Task.FromResult(Result(0));
        });

        var exception = await Assert.ThrowsAsync<VidoraException>(
            () => CreateService(runner, directory.Path).DownloadAsync(Request(directory.Path), new CapturedProgress(), CancellationToken.None));

        Assert.AreEqual(VidoraErrorCode.PostProcessingFailure, exception.Error.Code);
    }

    [TestMethod]
    public async Task DownloadAsync_FinalPathOutsideDestination_ThrowsDiskError()
    {
        using var directory = new TemporaryDirectory();
        var outside = Path.GetTempFileName();
        try
        {
            var runner = new FakeProcessRunner((_, output, _) =>
            {
                output?.Report(Line($"VIDORA_RESULT:{outside}"));
                return Task.FromResult(Result(0));
            });

            var exception = await Assert.ThrowsAsync<VidoraException>(
                () => CreateService(runner, directory.Path).DownloadAsync(Request(directory.Path), new CapturedProgress(), CancellationToken.None));

            Assert.AreEqual(VidoraErrorCode.DiskFailure, exception.Error.Code);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [TestMethod]
    public async Task DownloadAsync_ExistingFinalPath_ReturnsCompleted()
    {
        using var directory = new TemporaryDirectory();
        var finalPath = Path.Combine(directory.Path, "video.webm");
        var runner = new FakeProcessRunner((_, output, _) =>
        {
            File.WriteAllBytes(finalPath, [1, 2, 3]);
            output?.Report(Line($"VIDORA_RESULT:{finalPath}"));
            return Task.FromResult(Result(0));
        });

        var result = await CreateService(runner, directory.Path).DownloadAsync(
            Request(directory.Path),
            new CapturedProgress(),
            CancellationToken.None);

        Assert.AreEqual(DownloadStatus.Completed, result.Status);
        Assert.AreEqual(Path.GetFullPath(finalPath), result.FinalPath);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task DownloadAsync_Command_KeepsUrlAndPathsAsSeparateArguments()
    {
        using var directory = new TemporaryDirectory();
        var finalPath = Path.Combine(directory.Path, "video.mp4");
        ProcessSpec? captured = null;
        var runner = new FakeProcessRunner((spec, output, _) =>
        {
            captured = spec;
            File.WriteAllBytes(finalPath, [1]);
            output?.Report(Line($"VIDORA_RESULT:{finalPath}"));
            return Task.FromResult(Result(0));
        });
        var request = Request(directory.Path) with
        {
            SourceUrl = new Uri("https://youtube.com/watch?v=abc123&list=quoted%22value")
        };

        await CreateService(runner, directory.Path).DownloadAsync(request, new CapturedProgress(), CancellationToken.None);

        Assert.IsNotNull(captured);
        Assert.AreEqual(@"C:\tools\yt-dlp.exe", captured.FileName);
        CollectionAssert.Contains(captured.Arguments.ToArray(), "--ignore-config");
        CollectionAssert.Contains(captured.Arguments.ToArray(), "--no-playlist");
        CollectionAssert.Contains(captured.Arguments.ToArray(), "--newline");
        CollectionAssert.Contains(captured.Arguments.ToArray(), "--progress");
        CollectionAssert.Contains(captured.Arguments.ToArray(), "--no-overwrites");
        Assert.AreEqual(request.SourceUrl.OriginalString, captured.Arguments[^1]);
        Assert.AreEqual("--", captured.Arguments[^2]);
        Assert.AreEqual(1, captured.Arguments.Count(argument => argument == request.SourceUrl.OriginalString));

        var outputIndex = captured.Arguments.ToList().IndexOf("--output");
        var outputTemplate = captured.Arguments[outputIndex + 1];
        Assert.IsFalse(Path.IsPathRooted(outputTemplate));
        StringAssert.StartsWith(outputTemplate, "Video Việt [YouTube-abc123]");
        StringAssert.EndsWith(outputTemplate, ".%(ext)s");

        var progressIndex = captured.Arguments.ToList().IndexOf("--progress-template");
        StringAssert.StartsWith(captured.Arguments[progressIndex + 1], "download:VIDORA_PROGRESS:");
        var printIndex = captured.Arguments.ToList().IndexOf("--print");
        Assert.AreEqual("after_move:VIDORA_RESULT:%(filepath)s", captured.Arguments[printIndex + 1]);
        var pathValues = captured.Arguments
            .Select((argument, index) => (argument, index))
            .Where(item => item.argument == "--paths")
            .Select(item => captured.Arguments[item.index + 1])
            .ToArray();
        CollectionAssert.Contains(pathValues, $"home:{directory.Path}");
        CollectionAssert.Contains(
            pathValues,
            $"temp:{Path.Combine(directory.Path, "temp", request.Id.ToString("D"))}");
        Assert.IsFalse(Directory.Exists(Path.Combine(directory.Path, "temp", request.Id.ToString("D"))));
    }

    [TestMethod]
    public async Task DownloadAsync_LiteralPercentsInDestinationAndTitle_AreEscapedForOutputTemplate()
    {
        using var directory = new TemporaryDirectory();
        var destination = Directory.CreateDirectory(Path.Combine(directory.Path, "%(id)s")).FullName;
        var finalPath = Path.Combine(destination, "video.mp4");
        ProcessSpec? captured = null;
        var runner = new FakeProcessRunner((spec, output, _) =>
        {
            captured = spec;
            File.WriteAllBytes(finalPath, [1]);
            output?.Report(Line($"VIDORA_RESULT:{finalPath}"));
            return Task.FromResult(Result(0));
        });
        var request = Request(destination) with
        {
            Metadata = Request(destination).Metadata with { Title = "100% %(title)s" }
        };

        await CreateService(runner, directory.Path).DownloadAsync(request, new CapturedProgress(), CancellationToken.None);

        Assert.IsNotNull(captured);
        var outputIndex = captured.Arguments.ToList().IndexOf("--output");
        var outputTemplate = captured.Arguments[outputIndex + 1];
        Assert.AreEqual(
            "100% %(title)s [YouTube-abc123]".Replace("%", "%%", StringComparison.Ordinal) + ".%(ext)s",
            outputTemplate);
    }

    [TestMethod]
    public async Task DownloadAsync_WorkspaceCreationFails_ThrowsStableDiskError()
    {
        using var directory = new TemporaryDirectory();
        var request = Request(directory.Path);
        var workspace = new TaskWorkspaceService(
            Path.Combine(directory.Path, "temp"),
            new WindowsCanonicalPathResolver());
        workspace.Create(request.Id);
        var runner = new FakeProcessRunner((_, _, _) =>
            throw new AssertFailedException("Process must not start when workspace creation fails."));

        var exception = await Assert.ThrowsAsync<VidoraException>(() =>
            CreateService(runner, directory.Path, workspace).DownloadAsync(
                request,
                new CapturedProgress(),
                CancellationToken.None));

        Assert.AreEqual(VidoraErrorCode.DiskFailure, exception.Error.Code);
        Assert.AreEqual("Error.TempWorkspaceUnavailable", exception.Error.UserMessageKey);
    }

    [TestMethod]
    public async Task DownloadAsync_CancelledProcess_CleansOnlyTaskWorkspace()
    {
        using var directory = new TemporaryDirectory();
        var request = Request(directory.Path);
        var keepFile = Path.Combine(directory.Path, "keep.part");
        File.WriteAllText(keepFile, "user file");
        var runner = new FakeProcessRunner(async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new AssertFailedException("Cancellation should stop the process.");
        });
        using var cancellation = new CancellationTokenSource();
        var download = CreateService(runner, directory.Path).DownloadAsync(
            request,
            new CapturedProgress(),
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => download);
        Assert.IsFalse(Directory.Exists(Path.Combine(directory.Path, "temp", request.Id.ToString("D"))));
        Assert.IsTrue(File.Exists(keepFile));
    }

    private static DownloadService CreateService(
        IProcessRunner runner,
        string testRoot,
        TaskWorkspaceService? workspace = null) => new(
        runner,
        new AppToolPaths(@"C:\tools\yt-dlp.exe", @"C:\tools\ffmpeg\ffmpeg.exe"),
        workspace ?? new TaskWorkspaceService(
            Path.Combine(testRoot, "temp"),
            new WindowsCanonicalPathResolver()));

    private static DownloadRequest Request(string destination) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        new Uri("https://youtu.be/abc123"),
        new VideoMetadata(
            new Uri("https://youtu.be/abc123"),
            VideoPlatform.YouTube,
            "abc123",
            "Video Việt",
            "Author",
            TimeSpan.FromMinutes(2),
            null,
            [
                new VideoFormat("video", "mp4", 1920, 1080, "avc1", "none", 10_000_000),
                new VideoFormat("audio", "m4a", null, null, "none", "mp4a", 2_000_000)
            ]),
        QualityPreset.P1080,
        destination,
        new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));

    private static ProcessOutputLine Line(string text) => new(text, ProcessOutputStream.StandardOutput);

    private static ProcessResult Result(int exitCode) => new(exitCode, [], []);

    private sealed class CapturedProgress : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Values { get; } = [];

        public void Report(DownloadProgress value) => Values.Add(value);
    }

    private sealed class FakeProcessRunner(
        Func<ProcessSpec, IProgress<ProcessOutputLine>?, CancellationToken, Task<ProcessResult>> run) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessSpec spec,
            IProgress<ProcessOutputLine>? output,
            CancellationToken cancellationToken) => run(spec, output, cancellationToken);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-download-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
