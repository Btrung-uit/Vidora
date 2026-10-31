using System.Windows;
using System.Windows.Controls;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Features.Download;

namespace Vidora.Tests.Features.Download;

[TestClass]
public sealed class DownloadViewModelQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task StartDownloadCommand_ReadyItem_EnqueuesSnapshotAndRemembersValidDestination()
    {
        using var directory = new TemporaryDirectory();
        var queue = new FakeQueueService();
        var settings = new FakeSettingsService(new AppSettings(null));
        var viewModel = CreateViewModel(queue, settings);
        viewModel.Url = "https://youtu.be/abc123";
        viewModel.DestinationDirectory = directory.Path;
        viewModel.SelectedQuality = QualityPreset.P1080;
        await viewModel.DiscoverCommand.ExecuteAsync(null);

        await viewModel.StartDownloadCommand.ExecuteAsync(null);

        var queued = queue.Enqueued.Single();
        Assert.AreEqual(QualityPreset.P1080, queued.Quality);
        Assert.AreEqual(directory.Path, queued.DestinationDirectory);
        Assert.AreEqual(Now, queued.CreatedAt);
        Assert.AreEqual(directory.Path, settings.Saved.Single().LastDestinationDirectory);
    }

    [TestMethod]
    public async Task InitializeAsync_ExistingLastDestination_RestoresSelection()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(
            new FakeQueueService(),
            new FakeSettingsService(new AppSettings(directory.Path)));

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.AreEqual(directory.Path, viewModel.DestinationDirectory);
    }

    [TestMethod]
    public async Task QueueChanged_BackgroundThread_RefreshesProjectionThroughUiContext()
    {
        var context = new QueuedSynchronizationContext();
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(queue, new FakeSettingsService(new AppSettings(null)), context);
        var item = new DownloadQueueItem(
            Request(Guid.Parse("11111111-1111-1111-1111-111111111111"), @"C:\Videos"),
            DownloadStatus.Pending,
            null,
            null,
            null,
            0,
            false);

        await Task.Run(() => queue.Add(item));

        Assert.IsEmpty(viewModel.QueueItems);
        context.Drain();
        Assert.HasCount(1, viewModel.QueueItems);
        Assert.AreEqual(item.Request.Id, viewModel.QueueItems[0].Request.Id);
    }

    [STATestMethod]
    [DoNotParallelize]
    public void DownloadView_QueuedItem_RendersQueueAndResumeAction()
    {
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(queue, new FakeSettingsService(new AppSettings(null)));
        queue.Add(new DownloadQueueItem(
            Request(Guid.Parse("22222222-2222-2222-2222-222222222222"), @"C:\Videos"),
            DownloadStatus.Pending,
            null,
            null,
            null,
            0,
            false));
        var view = new DownloadView { DataContext = viewModel };
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Vidora;component/Resources/Strings.vi-VN.xaml", UriKind.Relative)
        });

        view.Measure(new Size(700, 800));
        view.Arrange(new Rect(0, 0, 700, 800));
        view.UpdateLayout();

        var queueList = view.FindName("QueueList") as ItemsControl;
        var resume = view.FindName("ResumeQueueButton") as Button;
        Assert.IsNotNull(queueList);
        Assert.HasCount(1, queueList.Items);
        Assert.IsNotNull(resume);
        Assert.AreEqual(view.FindResource("Queue.Resume"), resume.Content);
    }

    [STATestMethod]
    [DoNotParallelize]
    public void DownloadView_ActiveItemWithEmptyDiscovery_ShowsProgressAndCancel()
    {
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(queue, new FakeSettingsService(new AppSettings(null)));
        queue.Add(new DownloadQueueItem(
            Request(Guid.Parse("33333333-3333-3333-3333-333333333333"), @"C:\Videos"),
            DownloadStatus.Downloading,
            new DownloadProgress(25, 25, 100, 10, TimeSpan.FromSeconds(8), "downloading"),
            null,
            null,
            1,
            false));
        var view = CreateView(viewModel, 700, 600);

        var activePanel = view.FindName("ActiveDownloadPanel") as FrameworkElement;
        var cancel = view.FindName("CancelActiveDownloadButton") as Button;
        Assert.AreEqual(DownloadDiscoveryState.Empty, viewModel.State);
        Assert.IsNotNull(activePanel);
        Assert.AreEqual(Visibility.Visible, activePanel.Visibility);
        Assert.IsNotNull(cancel);
        Assert.AreEqual(viewModel.CancelDownloadCommand, cancel.Command);
    }

    [STATestMethod]
    [DoNotParallelize]
    public void DownloadView_ManyQueueItems_UsesBoundedScrollableRegion()
    {
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(queue, new FakeSettingsService(new AppSettings(null)));
        for (var index = 1; index <= 12; index++)
        {
            queue.Add(new DownloadQueueItem(
                Request(new Guid(index, 0, 0, new byte[8]), @"C:\Videos"),
                DownloadStatus.Pending,
                null,
                null,
                null,
                0,
                false));
        }

        var view = CreateView(viewModel, 700, 520);

        var scroll = view.FindName("QueueScrollViewer") as ScrollViewer;
        Assert.IsNotNull(scroll);
        Assert.IsLessThanOrEqualTo(220, scroll.ActualHeight);
        Assert.IsGreaterThan(0, scroll.ScrollableHeight);
    }

    private static DownloadView CreateView(DownloadViewModel viewModel, double width, double height)
    {
        var view = new DownloadView { DataContext = viewModel };
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Vidora;component/Resources/Strings.vi-VN.xaml", UriKind.Relative)
        });
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        return view;
    }

    private static DownloadViewModel CreateViewModel(
        FakeQueueService queue,
        FakeSettingsService settings,
        SynchronizationContext? context = null) => new(
            new FakeDiscoveryService(),
            queue,
            settings,
            new FixedTimeProvider(Now),
            context);

    private static DownloadRequest Request(Guid id, string destination) => new(
        id,
        new Uri("https://youtu.be/abc123"),
        Metadata(),
        QualityPreset.P720,
        destination,
        Now);

    private static VideoMetadata Metadata() => new(
        new Uri("https://youtu.be/abc123"),
        VideoPlatform.YouTube,
        "abc123",
        "Video",
        "Author",
        TimeSpan.FromMinutes(1),
        null,
        [new VideoFormat("18", "mp4", 1280, 720, "avc1", "mp4a", 1_000)]);

    private sealed class FakeDiscoveryService : IVideoDiscoveryService
    {
        public Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(Metadata());
    }

    private sealed class FakeQueueService : IDownloadQueueService
    {
        private readonly List<DownloadQueueItem> items = [];

        public IReadOnlyList<DownloadQueueItem> Items => items.ToArray();

        public List<DownloadRequest> Enqueued { get; } = [];

        public event EventHandler? Changed;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken)
        {
            Enqueued.Add(request);
            Add(new DownloadQueueItem(request, DownloadStatus.Pending, null, null, null, 0, false));
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CancelActiveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> RemovePendingAsync(Guid taskId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task PersistInterruptedAsync(
            Guid taskId,
            DownloadResult result,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void Add(DownloadQueueItem item)
        {
            items.Add(item);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public List<AppSettings> Saved { get; } = [];

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(settings);

        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken)
        {
            Saved.Add(value);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = new();

        public override void Post(SendOrPostCallback d, object? state) => callbacks.Enqueue((d, state));

        public void Drain()
        {
            while (callbacks.TryDequeue(out var work))
            {
                work.Callback(work.State);
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-download-queue-vm-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
