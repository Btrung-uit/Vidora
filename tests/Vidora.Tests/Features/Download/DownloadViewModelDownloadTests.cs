using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Features.Download;

namespace Vidora.Tests.Features.Download;

[TestClass]
public sealed class DownloadViewModelDownloadTests
{
    [TestMethod]
    public async Task StartDownloadCommand_Success_ForwardsProgressAndCompletes()
    {
        using var directory = new TemporaryDirectory();
        var completion = new TaskCompletionSource<DownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeDownloadService((_, progress, _) =>
        {
            progress.Report(new DownloadProgress(50, 100, 200, 25, TimeSpan.FromSeconds(4), "downloading"));
            return completion.Task;
        });
        var viewModel = await ReadyViewModel(service, directory.Path);

        var download = viewModel.StartDownloadCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsDownloading);
        Assert.IsFalse(viewModel.StartDownloadCommand.CanExecute(null));
        await WaitUntilAsync(() => viewModel.Progress?.Percent == 50);

        completion.SetResult(new DownloadResult(DownloadStatus.Completed, Path.Combine(directory.Path, "video.mp4"), null));
        await download;

        Assert.IsFalse(viewModel.IsDownloading);
        Assert.AreEqual(DownloadStatus.Completed, viewModel.LastDownloadStatus);
        Assert.AreEqual(DownloadStatus.Completed, viewModel.DownloadResult?.Status);
        Assert.IsNull(viewModel.DownloadError);
    }

    [TestMethod]
    public async Task StartDownloadCommand_WhileActive_DoesNotStartSecondDownload()
    {
        using var directory = new TemporaryDirectory();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<DownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = new FakeDownloadService((_, _, _) =>
        {
            calls++;
            started.TrySetResult();
            return completion.Task;
        });
        var viewModel = await ReadyViewModel(service, directory.Path);

        var first = viewModel.StartDownloadCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.StartDownloadCommand.Execute(null);

        Assert.AreEqual(1, calls);
        completion.SetResult(new DownloadResult(DownloadStatus.Completed, Path.Combine(directory.Path, "video.mp4"), null));
        await first;
    }

    [TestMethod]
    public async Task CancelDownloadCommand_ActiveDownload_CancelsAndMarksCancelled()
    {
        using var directory = new TemporaryDirectory();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeDownloadService(async (_, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new AssertFailedException("Cancellation should stop the active download.");
        });
        var viewModel = await ReadyViewModel(service, directory.Path);
        var download = viewModel.StartDownloadCommand.ExecuteAsync(null);
        await started.Task;

        viewModel.CancelDownloadCommand.Execute(null);
        await download;

        Assert.IsFalse(viewModel.IsDownloading);
        Assert.AreEqual(DownloadStatus.Cancelled, viewModel.LastDownloadStatus);
        Assert.IsNull(viewModel.DownloadResult);
        Assert.IsNull(viewModel.DownloadError);
    }

    private static async Task<DownloadViewModel> ReadyViewModel(
        IDownloadService downloadService,
        string destination)
    {
        var viewModel = new DownloadViewModel(
            new FakeDiscoveryService(),
            downloadService)
        {
            Url = "https://youtu.be/abc123",
            DestinationDirectory = destination,
            SelectedQuality = QualityPreset.P1080
        };
        await viewModel.DiscoverCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static VideoMetadata Metadata() => new(
        new Uri("https://youtu.be/abc123"),
        VideoPlatform.YouTube,
        "abc123",
        "Video",
        "Author",
        TimeSpan.FromMinutes(2),
        null,
        [
            new VideoFormat("video", "mp4", 1920, 1080, "avc1", "none", 10_000_000),
            new VideoFormat("audio", "m4a", null, null, "none", "mp4a", 2_000_000)
        ]);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeDiscoveryService : IVideoDiscoveryService
    {
        public Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(Metadata());
    }

    private sealed class FakeDownloadService(
        Func<DownloadRequest, IProgress<DownloadProgress>, CancellationToken, Task<DownloadResult>> download)
        : IDownloadService
    {
        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken) => download(request, progress, cancellationToken);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-download-vm-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
