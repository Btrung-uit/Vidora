using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Features.Download;

namespace Vidora.Tests.Features.Download;

[TestClass]
public sealed class DownloadViewModelDiscoveryTests
{
    [TestMethod]
    public async Task DiscoverCommand_Success_TransitionsEmptyToDiscoveringToReady()
    {
        var completion = new TaskCompletionSource<VideoMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new DownloadViewModel(new FakeDiscoveryService((_, _) => completion.Task), new StubDownloadService())
        {
            Url = "https://youtu.be/abc123"
        };

        var discovery = viewModel.DiscoverCommand.ExecuteAsync(null);

        Assert.AreEqual(DownloadDiscoveryState.Discovering, viewModel.State);
        Assert.IsNull(viewModel.Metadata);

        completion.SetResult(Metadata("abc123"));
        await discovery;

        Assert.AreEqual(DownloadDiscoveryState.Ready, viewModel.State);
        Assert.AreEqual("abc123", viewModel.Metadata?.MediaId);
        Assert.IsNull(viewModel.Error);
    }

    [TestMethod]
    public async Task CancelDiscoveryCommand_ActiveDiscovery_ReturnsToEmpty()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new DownloadViewModel(new FakeDiscoveryService(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new AssertFailedException("Cancellation should stop the pending discovery.");
        }), new StubDownloadService())
        {
            Url = "https://youtu.be/abc123"
        };
        var discovery = viewModel.DiscoverCommand.ExecuteAsync(null);
        await started.Task;

        viewModel.CancelDiscoveryCommand.Execute(null);
        await discovery;

        Assert.AreEqual(DownloadDiscoveryState.Empty, viewModel.State);
        Assert.IsNull(viewModel.Metadata);
        Assert.IsNull(viewModel.Error);
    }

    [TestMethod]
    public async Task DiscoverCommand_VidoraFailure_TransitionsToFailed()
    {
        var expectedError = new VidoraError(
            VidoraErrorCode.Unavailable,
            "Error.Unavailable",
            "Action.TryAnotherUrl",
            "Fixture unavailable.",
            false);
        var viewModel = new DownloadViewModel(new FakeDiscoveryService(
            (_, _) => Task.FromException<VideoMetadata>(new VidoraException(expectedError))), new StubDownloadService())
        {
            Url = "https://youtu.be/missing"
        };

        await viewModel.DiscoverCommand.ExecuteAsync(null);

        Assert.AreEqual(DownloadDiscoveryState.Failed, viewModel.State);
        Assert.AreSame(expectedError, viewModel.Error);
        Assert.IsNull(viewModel.Metadata);
    }

    [TestMethod]
    public async Task DiscoverCommand_SecondUrl_CancelsAndReplacesFirstDiscovery()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var viewModel = new DownloadViewModel(new FakeDiscoveryService(async (url, token) =>
        {
            calls++;
            if (calls == 1)
            {
                firstStarted.SetResult();
                using var registration = token.Register(() => firstCancelled.TrySetResult());
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return Metadata(url.EndsWith("second", StringComparison.Ordinal) ? "second" : "unexpected");
        }), new StubDownloadService())
        {
            Url = "https://youtu.be/first"
        };

        var firstDiscovery = viewModel.DiscoverCommand.ExecuteAsync(null);
        await firstStarted.Task;
        viewModel.Url = "https://youtu.be/second";
        var secondDiscovery = viewModel.DiscoverCommand.ExecuteAsync(null);

        await Task.WhenAll(firstDiscovery, secondDiscovery, firstCancelled.Task);

        Assert.AreEqual(2, calls);
        Assert.AreEqual(DownloadDiscoveryState.Ready, viewModel.State);
        Assert.AreEqual("second", viewModel.Metadata?.MediaId);
    }

    private static VideoMetadata Metadata(string id) => new(
        new Uri($"https://youtu.be/{id}"),
        VideoPlatform.YouTube,
        id,
        $"Video {id}",
        "Fixture author",
        TimeSpan.FromMinutes(2),
        new Uri("https://example.test/thumb.jpg"),
        []);

    private sealed class FakeDiscoveryService(
        Func<string, CancellationToken, Task<VideoMetadata>> discover) : IVideoDiscoveryService
    {
        public Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken) =>
            discover(url, cancellationToken);
    }

    private sealed class StubDownloadService : IDownloadService
    {
        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
