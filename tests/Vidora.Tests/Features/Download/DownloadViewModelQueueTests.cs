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
    [DataRow(DownloadStatus.Pending, true)]
    [DataRow(DownloadStatus.Failed, true)]
    [DataRow(DownloadStatus.Downloading, false)]
    [DataRow(DownloadStatus.Completed, false)]
    [DataRow(DownloadStatus.Cancelled, false)]
    [DataRow(DownloadStatus.Interrupted, false)]
    public void RemoveCommand_OnlyPendingAndFailedItemsCanBeRemoved(DownloadStatus status, bool expected)
    {
        var viewModel = CreateViewModel(new FakeQueueService(), new FakeSettingsService(new AppSettings(null)));
        var item = new DownloadQueueItem(Request(Guid.NewGuid(), @"C:\Videos"), status, null, null, null, 1, false);

        Assert.AreEqual(expected, viewModel.RemoveCommand.CanExecute(item));
        Assert.IsFalse(viewModel.RemoveCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task QueueActions_RetryAndClear_ForwardToServiceAndShowPersistenceFailure()
    {
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(queue, new FakeSettingsService(new AppSettings(null)));
        var failed = new DownloadQueueItem(Request(Guid.NewGuid(), @"C:\Videos"), DownloadStatus.Failed, null, null, null, 1, false);
        queue.Add(failed);

        Assert.IsTrue(viewModel.RetryQueueItemCommand.CanExecute(failed));
        Assert.IsFalse(viewModel.RetryQueueItemCommand.CanExecute(failed with { Status = DownloadStatus.Downloading }));
        await viewModel.RetryQueueItemCommand.ExecuteAsync(failed);
        Assert.AreEqual(failed.Request.Id, queue.RetriedId);
        Assert.IsTrue(viewModel.ClearFinishedCommand.CanExecute(null));
        await viewModel.ClearFinishedCommand.ExecuteAsync(null);
        Assert.AreEqual(1, queue.ClearCalls);
        Assert.IsTrue(viewModel.HasQueueOperationError);
    }

    [TestMethod]
    public async Task OpenCompletedOutput_UsesExistingPathsAndHandlesShellFailure()
    {
        using var directory = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "video.mp4");
        await File.WriteAllTextAsync(file, "keep");
        var queue = new FakeQueueService();
        var opened = new List<string>();
        var viewModel = new DownloadViewModel(new FakeDiscoveryService(), queue,
            new FakeSettingsService(new AppSettings(null)), new FixedTimeProvider(Now),
            openPath: path => opened.Add(path));
        var completed = new DownloadQueueItem(Request(Guid.NewGuid(), directory.Path), DownloadStatus.Completed, null, file, null, 1, false);

        viewModel.OpenQueueFileCommand.Execute(completed);
        viewModel.OpenQueueFolderCommand.Execute(completed);
        CollectionAssert.AreEqual(new[] { file, directory.Path }, opened);
        File.Delete(file);
        Assert.IsFalse(viewModel.OpenQueueFileCommand.CanExecute(completed));
        viewModel.OpenQueueFileCommand.Execute(completed);
        Assert.HasCount(2, opened);
        Assert.IsTrue(viewModel.HasQueueOperationError);

        var failing = new DownloadViewModel(new FakeDiscoveryService(), queue,
            new FakeSettingsService(new AppSettings(null)), new FixedTimeProvider(Now),
            openPath: _ => throw new System.ComponentModel.Win32Exception("No association"));
        failing.OpenQueueFolderCommand.Execute(completed);
        Assert.IsTrue(failing.HasQueueOperationError);
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
    [DataRow(DownloadStatus.Failed)]
    [DataRow(DownloadStatus.Completed)]
    public void DownloadView_QueueActions_BindToTheMatchingRow(DownloadStatus status)
    {
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(queue, new FakeSettingsService(new AppSettings(null)));
        var item = new DownloadQueueItem(Request(Guid.NewGuid(), @"C:\Videos"), status, null, null, null, 1, false);
        queue.Add(item);
        var view = CreateView(viewModel, 480, 360);
        var list = (ItemsControl)view.FindName("QueueList");
        var presenter = (ContentPresenter)list.ItemContainerGenerator.ContainerFromIndex(0);
        var retry = (Button)list.ItemTemplate.FindName("RetryQueueItemButton", presenter);
        var open = (Button)list.ItemTemplate.FindName("OpenQueueFileButton", presenter);

        Assert.AreEqual(viewModel.RetryQueueItemCommand, retry.Command);
        Assert.AreEqual(item, retry.CommandParameter);
        Assert.AreEqual(status == DownloadStatus.Failed ? Visibility.Visible : Visibility.Collapsed, retry.Visibility);
        Assert.AreEqual(viewModel.OpenQueueFileCommand, open.Command);
        Assert.AreEqual(status == DownloadStatus.Completed ? Visibility.Visible : Visibility.Collapsed, open.Visibility);
        Assert.AreEqual(viewModel.ClearFinishedCommand, ((Button)view.FindName("ClearFinishedButton")).Command);
        var outerScroll = (ScrollViewer)view.FindName("DownloadScrollViewer");
        Assert.IsGreaterThan(0, outerScroll.ScrollableHeight);
    }

    [STATestMethod]
    [DoNotParallelize]
    public void DownloadView_QualityLabels_PreservePresetSelectionBothWays()
    {
        var viewModel = CreateViewModel(new FakeQueueService(), new FakeSettingsService(new AppSettings(null)));
        var view = CreateView(viewModel, 700, 600);
        var selector = (ComboBox)view.FindName("QualitySelector");
        Assert.AreEqual("Tốt nhất", ((ComboBoxItem)selector.SelectedItem).Content);
        selector.SelectedValue = QualityPreset.P480;
        Assert.AreEqual(QualityPreset.P480, viewModel.SelectedQuality);
        Assert.AreEqual("480p", ((ComboBoxItem)selector.SelectedItem).Content);
        viewModel.SelectedQuality = QualityPreset.P1080;
        view.UpdateLayout();
        Assert.AreEqual("1080p", ((ComboBoxItem)selector.SelectedItem).Content);
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
        public bool HasHistoryPersistenceError => false;

        private readonly List<DownloadQueueItem> items = [];

        public IReadOnlyList<DownloadQueueItem> Items => items.ToArray();

        public List<DownloadRequest> Enqueued { get; } = [];

        public Guid? RetriedId { get; private set; }

        public int ClearCalls { get; private set; }

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

        public Task<bool> RemoveAsync(Guid taskId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> RetryAsync(Guid taskId, CancellationToken cancellationToken)
        {
            RetriedId = taskId;
            return Task.FromResult(true);
        }

        public Task<bool> ClearFinishedAsync(CancellationToken cancellationToken)
        {
            ClearCalls++;
            throw new IOException("Queue locked");
        }

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

        public async Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct) =>
            await SaveAsync(update(await LoadAsync(ct)), ct);

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
