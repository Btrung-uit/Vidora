using System.Windows;
using System.Windows.Controls;
using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Features.Download;

namespace Vidora.Tests.Features.Download;

[TestClass]
public sealed class DownloadViewModelDownloadTests
{
    [TestMethod]
    public void QueueChanged_DownloadingItem_ProjectsProgress()
    {
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(queue);
        var progress = new DownloadProgress(50, 100, 200, 25, TimeSpan.FromSeconds(4), "downloading");

        queue.SetItem(Item(DownloadStatus.Downloading, progress));

        Assert.IsTrue(viewModel.IsDownloading);
        Assert.AreEqual(DownloadStatus.Downloading, viewModel.LastDownloadStatus);
        Assert.AreEqual(50, viewModel.Progress?.Percent);
    }

    [TestMethod]
    public async Task CancelDownloadCommand_ActiveItem_ProjectsCancelledState()
    {
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(queue);
        queue.SetItem(Item(DownloadStatus.Downloading));

        await viewModel.CancelDownloadCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsDownloading);
        Assert.AreEqual(DownloadStatus.Cancelled, viewModel.LastDownloadStatus);
    }

    [TestMethod]
    public async Task DiscoverCommand_WhileDownloading_CanPrepareNextQueueItem()
    {
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(queue, new FakeDiscoveryService());
        queue.SetItem(Item(DownloadStatus.Downloading));
        viewModel.Url = "https://youtu.be/next";

        await viewModel.DiscoverCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.DiscoverCommand.CanExecute(null));
        Assert.AreEqual(DownloadDiscoveryState.Ready, viewModel.State);
        Assert.AreEqual("next", viewModel.Metadata?.MediaId);
    }

    [TestMethod]
    public void QueueChanged_FailedItemBeforePendingTail_PreservesFailureProjection()
    {
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(queue);
        var failed = Item(DownloadStatus.Failed);
        var pending = Item(DownloadStatus.Pending) with
        {
            Request = Item(DownloadStatus.Pending).Request with { Id = Guid.NewGuid() }
        };

        queue.SetItems(failed, pending);

        Assert.AreEqual(DownloadStatus.Failed, viewModel.LastDownloadStatus);
        Assert.AreEqual(VidoraErrorCode.Unknown, viewModel.DownloadError?.Code);
        Assert.AreEqual(DownloadStatus.Failed, viewModel.DownloadResult?.Status);
    }

    [STATestMethod]
    [DoNotParallelize]
    public void DownloadView_TerminalStates_ShowLocalizedResults()
    {
        foreach (var (terminalStatus, expectedResourceKey) in new[]
        {
            (DownloadStatus.Completed, "Download.Status.Completed"),
            (DownloadStatus.Failed, "Download.Status.Failed"),
            (DownloadStatus.Cancelled, "Download.Status.Cancelled")
        })
        {
            var queue = new FakeQueueService();
            var viewModel = CreateViewModel(queue);
            queue.SetItem(Item(terminalStatus));
            var view = new DownloadView { DataContext = viewModel };
            view.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Vidora;component/Resources/Strings.vi-VN.xaml", UriKind.Relative)
            });
            view.Measure(new Size(700, 700));
            view.Arrange(new Rect(0, 0, 700, 700));
            view.UpdateLayout();

            var panel = (StackPanel)view.FindName("DownloadResultPanel");
            var status = (TextBlock)view.FindName("DownloadStatusText");
            Assert.AreEqual(Visibility.Visible, panel.Visibility);
            Assert.AreEqual(view.FindResource(expectedResourceKey), status.Text);
        }
    }

    private static DownloadViewModel CreateViewModel(
        FakeQueueService queue,
        IVideoDiscoveryService? discoveryService = null) => new(
            discoveryService ?? new FakeDiscoveryService(),
            queue,
            new StubSettingsService(),
            TimeProvider.System);

    private static DownloadQueueItem Item(
        DownloadStatus status,
        DownloadProgress? progress = null)
    {
        var error = status == DownloadStatus.Failed
            ? new VidoraError(VidoraErrorCode.Unknown, "Error.DownloadFailed", "Action.TryAgain", "test", true)
            : null;
        return new DownloadQueueItem(
            new DownloadRequest(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                new Uri("https://youtu.be/active"),
                Metadata("active"),
                QualityPreset.P720,
                @"C:\Videos",
                DateTimeOffset.UtcNow),
            status,
            progress,
            status == DownloadStatus.Completed ? @"C:\Videos\video.mp4" : null,
            error,
            1,
            false);
    }

    private static VideoMetadata Metadata(string id) => new(
        new Uri($"https://youtu.be/{id}"),
        VideoPlatform.YouTube,
        id,
        "Video",
        "Author",
        TimeSpan.FromMinutes(2),
        null,
        [new VideoFormat("18", "mp4", 1280, 720, "avc1", "mp4a", 1_000)]);

    private sealed class FakeDiscoveryService : IVideoDiscoveryService
    {
        public Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(Metadata(url.EndsWith("next", StringComparison.Ordinal) ? "next" : "active"));
    }

    private sealed class FakeQueueService : IDownloadQueueService
    {
        private IReadOnlyList<DownloadQueueItem> items = [];

        public IReadOnlyList<DownloadQueueItem> Items => items;

        public event EventHandler? Changed;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CancelActiveAsync(CancellationToken cancellationToken)
        {
            var active = items.FirstOrDefault(value => value.Status == DownloadStatus.Downloading);
            if (active is not null)
            {
                SetItem(active with { Status = DownloadStatus.Cancelled, Progress = null });
            }

            return Task.CompletedTask;
        }

        public Task<bool> RemovePendingAsync(Guid taskId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task PersistInterruptedAsync(
            Guid taskId,
            DownloadResult result,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void SetItem(DownloadQueueItem value)
        {
            SetItems(value);
        }

        public void SetItems(params DownloadQueueItem[] values)
        {
            items = values;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AppSettings(null));

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
