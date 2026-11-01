using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Features.History;
using System.Windows;
using System.Windows.Controls;

namespace Vidora.Tests.Features.History;

[TestClass]
public sealed class HistoryViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 11, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task InitializeAsync_MissingOutput_ComputesMissingWithoutMutatingHistory()
    {
        var item = Item(finalPath: @"Z:\missing\video.mp4");
        var history = new FakeHistoryService([item]);
        var viewModel = CreateViewModel(history: history);

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.Items.Single().IsMissing);
        Assert.AreEqual(0, history.AddCalls);
        Assert.AreEqual(0, history.ClearCalls);
    }

    [TestMethod]
    public async Task HistoryItem_FileDeletedAfterLoad_BecomesMissingWithoutMutatingHistory()
    {
        using var directory = new TemporaryDirectory();
        var mediaPath = Path.Combine(directory.Path, "video.mp4");
        await File.WriteAllTextAsync(mediaPath, "media", CancellationToken.None);
        var history = new FakeHistoryService([Item(finalPath: mediaPath, destination: directory.Path)]);
        var viewModel = CreateViewModel(history: history);
        await viewModel.InitializeAsync(CancellationToken.None);
        var row = viewModel.Items.Single();
        Assert.IsFalse(row.IsMissing);

        File.Delete(mediaPath);

        Assert.IsTrue(row.IsMissing);
        Assert.AreEqual(0, history.AddCalls);
        Assert.AreEqual(0, history.ClearCalls);
    }

    [TestMethod]
    public async Task OpenFileCommand_MissingOutput_IsDisabled()
    {
        var opened = new List<string>();
        var viewModel = CreateViewModel(openPath: opened.Add);
        await viewModel.InitializeAsync(CancellationToken.None);
        var row = viewModel.Items.Single();

        var canOpen = viewModel.OpenFileCommand.CanExecute(row);
        viewModel.OpenFileCommand.Execute(row);

        Assert.IsFalse(canOpen);
        Assert.IsEmpty(opened);
    }

    [TestMethod]
    public async Task OpenFolderCommand_MissingOutput_OpensExistingParent()
    {
        using var directory = new TemporaryDirectory();
        var missingFile = Path.Combine(directory.Path, "deleted.mp4");
        var opened = new List<string>();
        var viewModel = CreateViewModel(
            history: new FakeHistoryService([Item(finalPath: missingFile, destination: directory.Path)]),
            openPath: opened.Add);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.OpenFolderCommand.Execute(viewModel.Items.Single());

        CollectionAssert.AreEqual(new[] { directory.Path }, opened);
    }

    [TestMethod]
    public async Task CopyUrlCommand_Item_CopiesSourceUrl()
    {
        var copied = new List<string>();
        var viewModel = CreateViewModel(copyText: copied.Add);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.CopyUrlCommand.Execute(viewModel.Items.Single());

        CollectionAssert.AreEqual(new[] { "https://youtu.be/abc123" }, copied);
    }

    [TestMethod]
    public async Task RetryCommand_Item_EnqueuesNewRequestWithCapturedDownloadInputs()
    {
        var queue = new FakeQueueService();
        var item = Item(quality: QualityPreset.P1080, destination: @"C:\Captured");
        var viewModel = CreateViewModel(history: new FakeHistoryService([item]), queue: queue);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RetryCommand.ExecuteAsync(viewModel.Items.Single());

        var request = queue.Enqueued.Single();
        Assert.AreNotEqual(item.TaskId, request.Id);
        Assert.AreEqual(item.SourceUrl, request.SourceUrl);
        Assert.AreEqual(item.MediaId, request.Metadata.MediaId);
        Assert.AreEqual(item.Title, request.Metadata.Title);
        Assert.AreEqual(item.Platform, request.Metadata.Platform);
        CollectionAssert.AreEqual(item.Formats.ToArray(), request.Metadata.Formats.ToArray());
        Assert.AreEqual(QualityPreset.P1080, request.Quality);
        Assert.AreEqual(@"C:\Captured", request.DestinationDirectory);
        Assert.AreEqual(Now, request.CreatedAt);
    }

    [TestMethod]
    public async Task ClearCommand_ConfirmationDeclined_KeepsHistory()
    {
        var history = new FakeHistoryService([Item()]);
        var viewModel = CreateViewModel(history: history, confirmClear: () => false);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.ClearCommand.ExecuteAsync(null);

        Assert.AreEqual(0, history.ClearCalls);
        Assert.HasCount(1, viewModel.Items);
    }

    [TestMethod]
    public async Task ClearCommand_ConfirmationAccepted_ClearsHistoryAndProjection()
    {
        var history = new FakeHistoryService([Item()]);
        var viewModel = CreateViewModel(history: history, confirmClear: () => true);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.ClearCommand.ExecuteAsync(null);

        Assert.AreEqual(1, history.ClearCalls);
        Assert.IsEmpty(viewModel.Items);
    }

    [TestMethod]
    public async Task QueueChanged_BackgroundThread_RefreshesHistoryThroughUiContext()
    {
        var context = new QueuedSynchronizationContext();
        var first = Item();
        var second = Item() with { TaskId = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var history = new FakeHistoryService([first]);
        var queue = new FakeQueueService();
        var viewModel = CreateViewModel(history, queue, synchronizationContext: context);
        await viewModel.InitializeAsync(CancellationToken.None);
        await history.AddAsync(second, CancellationToken.None);

        await Task.Run(queue.NotifyChanged);

        Assert.HasCount(1, viewModel.Items);
        context.Drain();
        Assert.HasCount(2, viewModel.Items);
    }

    [STATestMethod]
    [DoNotParallelize]
    public void HistoryView_LoadedItem_RendersListAndClearAction()
    {
        var viewModel = CreateViewModel();
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var view = new HistoryView { DataContext = viewModel };
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Vidora;component/Resources/Strings.vi-VN.xaml", UriKind.Relative)
        });

        view.Measure(new Size(700, 500));
        view.Arrange(new Rect(0, 0, 700, 500));
        view.UpdateLayout();

        var list = view.FindName("HistoryList") as ItemsControl;
        var clear = view.FindName("ClearHistoryButton") as Button;
        Assert.IsNotNull(list);
        Assert.HasCount(1, list.Items);
        Assert.IsNotNull(clear);
        Assert.AreEqual(view.FindResource("History.Clear"), clear.Content);
        Assert.AreEqual(viewModel.ClearCommand, clear.Command);
    }

    private static HistoryViewModel CreateViewModel(
        FakeHistoryService? history = null,
        FakeQueueService? queue = null,
        Action<string>? openPath = null,
        Action<string>? copyText = null,
        Func<bool>? confirmClear = null,
        SynchronizationContext? synchronizationContext = null) => new(
            history ?? new FakeHistoryService([Item()]),
            queue ?? new FakeQueueService(),
            openPath ?? (_ => { }),
            copyText ?? (_ => { }),
            confirmClear ?? (() => true),
            new FixedTimeProvider(Now),
            synchronizationContext);

    private static DownloadHistoryItem Item(
        string? finalPath = @"Z:\missing\video.mp4",
        QualityPreset quality = QualityPreset.P720,
        string destination = @"C:\Videos") => new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            1,
            Now.AddMinutes(-1),
            new Uri("https://youtu.be/abc123"),
            "abc123",
            "Video title",
            VideoPlatform.YouTube,
            [new VideoFormat("18", "mp4", 1280, 720, "avc1", "mp4a", 1_000)],
            quality,
            destination,
            DownloadStatus.Completed,
            finalPath,
            (VidoraErrorCode?)null);

    private sealed class FakeHistoryService(IEnumerable<DownloadHistoryItem> initialItems) : IHistoryService
    {
        private readonly List<DownloadHistoryItem> items = [.. initialItems];

        public int AddCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public Task<IReadOnlyList<DownloadHistoryItem>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownloadHistoryItem>>(items.ToArray());

        public Task AddAsync(DownloadHistoryItem item, CancellationToken cancellationToken)
        {
            AddCalls++;
            items.Add(item);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            ClearCalls++;
            items.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeQueueService : IDownloadQueueService
    {
        public IReadOnlyList<DownloadQueueItem> Items => [];

        public List<DownloadRequest> Enqueued { get; } = [];

        public event EventHandler? Changed;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken)
        {
            Enqueued.Add(request);
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

        public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
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
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-history-vm-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
