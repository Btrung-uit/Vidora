using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Features.Settings;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Dependencies;

[TestClass]
public sealed class DependencyServiceTests
{
    [TestMethod]
    public async Task CheckAsync_SuccessfulVersionCommands_MarksBothComponentsReady()
    {
        using var tools = new TemporaryToolFiles();
        var runner = new FakeProcessRunner((spec, _) => Task.FromResult(
            spec.FileName == tools.Paths.YtDlpPath
                ? Result(0, "2026.10.30")
                : Result(0, "ffmpeg version 7.1.1-full_build www.gyan.dev")));

        var report = await new DependencyService(runner, tools.Paths).CheckAsync(CancellationToken.None);

        Assert.AreEqual(DependencyStatus.Ready, report.YtDlp.Status);
        Assert.AreEqual("2026.10.30", report.YtDlp.Version);
        Assert.AreEqual(DependencyStatus.Ready, report.Ffmpeg.Status);
        Assert.AreEqual("7.1.1-full_build", report.Ffmpeg.Version);
        Assert.IsTrue(report.IsReady);
    }

    [TestMethod]
    public async Task CheckAsync_MissingExecutables_MarksBothComponentsMissing()
    {
        var paths = new AppToolPaths(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}-yt-dlp.exe"),
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}-ffmpeg.exe"));
        var runner = new FakeProcessRunner((_, _) => throw new AssertFailedException("Missing tools must not be started."));

        var report = await new DependencyService(runner, paths).CheckAsync(CancellationToken.None);

        Assert.AreEqual(DependencyStatus.Missing, report.YtDlp.Status);
        Assert.AreEqual(DependencyStatus.Missing, report.Ffmpeg.Status);
        Assert.IsFalse(report.IsReady);
    }

    [TestMethod]
    public async Task CheckAsync_NonZeroExitCodes_MarksBothComponentsInvalid()
    {
        using var tools = new TemporaryToolFiles();
        var runner = new FakeProcessRunner((_, _) => Task.FromResult(Result(1, "broken")));

        var report = await new DependencyService(runner, tools.Paths).CheckAsync(CancellationToken.None);

        Assert.AreEqual(DependencyStatus.Invalid, report.YtDlp.Status);
        Assert.AreEqual(DependencyStatus.Invalid, report.Ffmpeg.Status);
    }

    [TestMethod]
    public async Task CheckAsync_TimedOutCommands_MarksBothComponentsInvalid()
    {
        using var tools = new TemporaryToolFiles();
        var runner = new FakeProcessRunner((_, token) => Task.FromException<ProcessResult>(
            new OperationCanceledException(token)));

        var report = await new DependencyService(runner, tools.Paths).CheckAsync(CancellationToken.None);

        Assert.AreEqual(DependencyStatus.Invalid, report.YtDlp.Status);
        Assert.AreEqual(DependencyStatus.Invalid, report.Ffmpeg.Status);
    }

    [TestMethod]
    public async Task CheckDependenciesCommand_ExposesReadinessWithoutBlockingCaller()
    {
        var completion = new TaskCompletionSource<DependencyReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new SettingsViewModel(new FakeDependencyService(completion.Task));

        var checkTask = viewModel.CheckDependenciesCommand.ExecuteAsync(null);

        Assert.IsFalse(checkTask.IsCompleted);
        Assert.IsTrue(viewModel.IsChecking);
        Assert.IsFalse(viewModel.IsReady);

        completion.SetResult(ReadyReport());
        await checkTask;

        Assert.IsFalse(viewModel.IsChecking);
        Assert.IsTrue(viewModel.IsReady);
    }

    private static ProcessResult Result(int exitCode, params string[] standardOutput) =>
        new(exitCode, standardOutput, []);

    private static DependencyReport ReadyReport() => new(
        new DependencyComponentReport(DependencyStatus.Ready, "2026.10.30"),
        new DependencyComponentReport(DependencyStatus.Ready, "7.1.1"));

    private sealed class FakeProcessRunner(
        Func<ProcessSpec, CancellationToken, Task<ProcessResult>> run) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessSpec spec,
            IProgress<ProcessOutputLine>? output,
            CancellationToken cancellationToken) => run(spec, cancellationToken);
    }

    private sealed class FakeDependencyService(Task<DependencyReport> reportTask) : IDependencyService
    {
        public Task<DependencyReport> CheckAsync(CancellationToken cancellationToken) => reportTask;
    }

    private sealed class TemporaryToolFiles : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("vidora-dependency-test-");

        public TemporaryToolFiles()
        {
            Paths = new AppToolPaths(
                Path.Combine(directory.FullName, "yt-dlp.exe"),
                Path.Combine(directory.FullName, "ffmpeg.exe"));
            File.WriteAllBytes(Paths.YtDlpPath, []);
            File.WriteAllBytes(Paths.FfmpegPath, []);
        }

        public AppToolPaths Paths { get; }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
