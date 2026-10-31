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
        var viewModel = CreateViewModel(new FakeDiscoveryService((_, _) => completion.Task));
        viewModel.Url = "https://youtu.be/abc123";

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
        var viewModel = CreateViewModel(new FakeDiscoveryService(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new AssertFailedException("Cancellation should stop the pending discovery.");
        }));
        viewModel.Url = "https://youtu.be/abc123";
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
        var viewModel = CreateViewModel(new FakeDiscoveryService(
            (_, _) => Task.FromException<VideoMetadata>(new VidoraException(expectedError))));
        viewModel.Url = "https://youtu.be/missing";

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
        var viewModel = CreateViewModel(new FakeDiscoveryService(async (url, token) =>
        {
            calls++;
            if (calls == 1)
            {
                firstStarted.SetResult();
                using var registration = token.Register(() => firstCancelled.TrySetResult());
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return Metadata(url.EndsWith("second", StringComparison.Ordinal) ? "second" : "unexpected");
        }));
        viewModel.Url = "https://youtu.be/first";

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

    private static DownloadViewModel CreateViewModel(IVideoDiscoveryService discoveryService) => new(
        discoveryService,
        new StubQueueService(),
        new StubSettingsService(),
        TimeProvider.System);

    private sealed class FakeDiscoveryService(
        Func<string, CancellationToken, Task<VideoMetadata>> discover) : IVideoDiscoveryService
    {
        public Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken) =>
            discover(url, cancellationToken);
    }

    private sealed class StubQueueService : IDownloadQueueService
    {
        public IReadOnlyList<DownloadQueueItem> Items => [];

        public event EventHandler? Changed;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CancelActiveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> RemovePendingAsync(Guid taskId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task PersistInterruptedAsync(Guid taskId, DownloadResult result, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AppSettings(null));

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
