using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Persistence;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class DownloadQueueServiceTests
{
    [TestMethod]
    public async Task EnqueueAsync_TwoItems_RunsFifoWithConcurrencyOne()
    {
        using var context = new QueueTestContext();
        var completions = new Dictionary<Guid, TaskCompletionSource<DownloadResult>>();
        var calls = new List<Guid>();
        var active = 0;
        var maximumActive = 0;
        var service = await context.CreateService((request, _, _) =>
        {
            calls.Add(request.Id);
            maximumActive = Math.Max(maximumActive, Interlocked.Increment(ref active));
            var completion = new TaskCompletionSource<DownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            completions.Add(request.Id, completion);
            return completion.Task.ContinueWith(
                task =>
                {
                    Interlocked.Decrement(ref active);
                    return task.GetAwaiter().GetResult();
                },
                TaskScheduler.Default);
        });
        var first = Request("11111111-1111-1111-1111-111111111111");
        var second = Request("22222222-2222-2222-2222-222222222222");

        await service.EnqueueAsync(first, CancellationToken.None);
        await service.EnqueueAsync(second, CancellationToken.None);
        await WaitUntilAsync(() => calls.Count == 1);

        CollectionAssert.AreEqual(new[] { first.Id }, calls);
        completions[first.Id].SetResult(Completed(first));
        await WaitUntilAsync(() => calls.Count == 2);
        CollectionAssert.AreEqual(new[] { first.Id, second.Id }, calls);
        Assert.AreEqual(1, maximumActive);
        completions[second.Id].SetResult(Completed(second));
        await WaitUntilAsync(() => service.Items.All(item => item.Status == DownloadStatus.Completed));
    }

    [TestMethod]
    public async Task EnqueueAsync_Request_StoresIndependentSnapshot()
    {
        using var context = new QueueTestContext();
        var formats = new List<VideoFormat>
        {
            new("18", "mp4", 1280, 720, "avc1", "mp4a", 1_000)
        };
        var completion = new TaskCompletionSource<DownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = await context.CreateService((_, _, _) => completion.Task);
        var request = Request("33333333-3333-3333-3333-333333333333", formats, QualityPreset.P720, @"C:\First");

        await service.EnqueueAsync(request, CancellationToken.None);
        formats.Clear();
        request = request with { Quality = QualityPreset.Best, DestinationDirectory = @"C:\Second" };

        var queued = service.Items.Single();
        Assert.AreEqual(QualityPreset.P720, queued.Request.Quality);
        Assert.AreEqual(@"C:\First", queued.Request.DestinationDirectory);
        Assert.HasCount(1, queued.Request.Metadata.Formats);
        completion.SetResult(Completed(queued.Request));
        await WaitUntilAsync(() => service.Items.Single().Status == DownloadStatus.Completed);
    }

    [TestMethod]
    public async Task RemovePendingAsync_ActiveAndPending_RemovesOnlyPending()
    {
        using var context = new QueueTestContext();
        var completion = new TaskCompletionSource<DownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = await context.CreateService((_, _, _) => completion.Task);
        var active = Request("44444444-4444-4444-4444-444444444444");
        var pending = Request("55555555-5555-5555-5555-555555555555");
        await service.EnqueueAsync(active, CancellationToken.None);
        await service.EnqueueAsync(pending, CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Any(item => item.Status == DownloadStatus.Downloading));

        var removedActive = await service.RemovePendingAsync(active.Id, CancellationToken.None);
        var removedPending = await service.RemovePendingAsync(pending.Id, CancellationToken.None);

        Assert.IsFalse(removedActive);
        Assert.IsTrue(removedPending);
        Assert.IsFalse(service.Items.Any(item => item.Request.Id == pending.Id));
        completion.SetResult(Completed(active));
        await WaitUntilAsync(() => service.Items.Single().Status == DownloadStatus.Completed);
    }

    [TestMethod]
    public async Task DownloadFailure_StopsQueueWithoutAutomaticRetry()
    {
        using var context = new QueueTestContext();
        var calls = 0;
        var service = await context.CreateService((request, _, _) =>
        {
            calls++;
            return Task.FromResult(request.Id == Guid.Parse("66666666-6666-6666-6666-666666666666")
                ? Failed()
                : Completed(request));
        });
        var first = Request("66666666-6666-6666-6666-666666666666");
        var second = Request("77777777-7777-7777-7777-777777777777");

        await service.EnqueueAsync(first, CancellationToken.None);
        await service.EnqueueAsync(second, CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Any(item => item.Status == DownloadStatus.Failed));
        await Task.Delay(100);

        Assert.AreEqual(1, calls);
        Assert.AreEqual(DownloadStatus.Pending, service.Items.Single(item => item.Request.Id == second.Id).Status);
    }

    [TestMethod]
    public async Task ResumeAsync_AfterFailure_StartsNextPendingItem()
    {
        using var context = new QueueTestContext();
        var calls = new List<Guid>();
        var first = Request("88888888-8888-8888-8888-888888888888");
        var second = Request("99999999-9999-9999-9999-999999999999");
        var service = await context.CreateService((request, _, _) =>
        {
            calls.Add(request.Id);
            return Task.FromResult(request.Id == first.Id ? Failed() : Completed(request));
        });
        await service.EnqueueAsync(first, CancellationToken.None);
        await service.EnqueueAsync(second, CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Any(item => item.Status == DownloadStatus.Failed));

        await service.ResumeAsync(CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Single(item => item.Request.Id == second.Id).Status == DownloadStatus.Completed);

        CollectionAssert.AreEqual(new[] { first.Id, second.Id }, calls);
    }

    [TestMethod]
    public async Task CancelActiveAsync_StopsQueueUntilResume()
    {
        using var context = new QueueTestContext();
        var calls = new List<Guid>();
        var first = Request("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var second = Request("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var service = await context.CreateService(async (request, _, token) =>
        {
            calls.Add(request.Id);
            if (request.Id == first.Id)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return Completed(request);
        });
        await service.EnqueueAsync(first, CancellationToken.None);
        await service.EnqueueAsync(second, CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Any(item => item.Status == DownloadStatus.Downloading));

        await service.CancelActiveAsync(CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Single(item => item.Request.Id == first.Id).Status == DownloadStatus.Cancelled);
        Assert.AreEqual(DownloadStatus.Pending, service.Items.Single(item => item.Request.Id == second.Id).Status);
        CollectionAssert.AreEqual(new[] { first.Id }, calls);

        await service.ResumeAsync(CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Single(item => item.Request.Id == second.Id).Status == DownloadStatus.Completed);
        CollectionAssert.AreEqual(new[] { first.Id, second.Id }, calls);
    }

    [TestMethod]
    public async Task EnqueueAsync_DuplicateUrl_MarksWarningButAllowsBothItems()
    {
        using var context = new QueueTestContext();
        var firstCompletion = new TaskCompletionSource<DownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var service = await context.CreateService((request, _, token) =>
            request.Id == firstId ? firstCompletion.Task : WaitForCancellation(token));
        var first = Request("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var second = Request("dddddddd-dddd-dddd-dddd-dddddddddddd") with { SourceUrl = first.SourceUrl };
        second = second with { Metadata = second.Metadata with { SourceUrl = first.SourceUrl } };

        await service.EnqueueAsync(first, CancellationToken.None);
        await service.EnqueueAsync(second, CancellationToken.None);

        Assert.HasCount(2, service.Items);
        Assert.IsFalse(service.Items[0].HasDuplicateUrlWarning);
        Assert.IsTrue(service.Items[1].HasDuplicateUrlWarning);
        firstCompletion.SetResult(Completed(first));
        await WaitUntilAsync(() => service.Items.Any(item => item.Request.Id == second.Id && item.Status == DownloadStatus.Downloading));
        await service.CancelActiveAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task InitializeAsync_RestoredPendingItem_DoesNotStartUntilResume()
    {
        using var context = new QueueTestContext();
        var restored = Request("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        await context.Store.SaveAsync(
            [new DownloadQueueItem(restored, DownloadStatus.Pending, null, null, null, 0, false)],
            CancellationToken.None);
        var calls = 0;
        var service = await context.CreateService((request, _, _) =>
        {
            calls++;
            return Task.FromResult(Completed(request));
        });

        await Task.Delay(100);
        Assert.AreEqual(0, calls);
        Assert.AreEqual(DownloadStatus.Pending, service.Items.Single().Status);

        await service.ResumeAsync(CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Single().Status == DownloadStatus.Completed);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task PersistInterruptedAsync_CancelledItem_PersistsInterruptedState()
    {
        using var context = new QueueTestContext();
        var request = Request("ffffffff-ffff-ffff-ffff-ffffffffffff");
        await context.Store.SaveAsync(
            [new DownloadQueueItem(request, DownloadStatus.Cancelled, null, null, null, 1, false)],
            CancellationToken.None);
        var service = await context.CreateService((_, _, _) =>
            throw new AssertFailedException("Restored queue must remain stopped."));

        await service.PersistInterruptedAsync(
            request.Id,
            new DownloadResult(DownloadStatus.Interrupted, null, null),
            CancellationToken.None);
        var restored = await context.Store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(DownloadStatus.Interrupted, service.Items.Single().Status);
        Assert.AreEqual(DownloadStatus.Interrupted, restored.Single().Status);
    }

    private static DownloadRequest Request(
        string id,
        IReadOnlyList<VideoFormat>? formats = null,
        QualityPreset quality = QualityPreset.P720,
        string destination = @"C:\Videos")
    {
        var taskId = Guid.Parse(id);
        var url = new Uri($"https://youtu.be/{taskId:N}");
        return new DownloadRequest(
            taskId,
            url,
            new VideoMetadata(
                url,
                VideoPlatform.YouTube,
                taskId.ToString("N"),
                "Video",
                "Author",
                TimeSpan.FromMinutes(1),
                null,
                formats ?? [new VideoFormat("18", "mp4", 1280, 720, "avc1", "mp4a", 1_000)]),
            quality,
            destination,
            new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero));
    }

    private static DownloadResult Completed(DownloadRequest request) =>
        new(DownloadStatus.Completed, Path.Combine(request.DestinationDirectory, $"{request.Id:N}.mp4"), null);

    private static DownloadResult Failed() => new(
        DownloadStatus.Failed,
        null,
        new VidoraError(VidoraErrorCode.Unknown, "Error.DownloadFailed", "Action.TryAgain", "test", true));

    private static async Task<DownloadResult> WaitForCancellation(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new AssertFailedException("The queued download should have been cancelled.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class QueueTestContext : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("vidora-queue-service-test-").FullName;

        public QueueStore Store => new(Path.Combine(directory, "queue.json"), TimeProvider.System);

        public async Task<DownloadQueueService> CreateService(
            Func<DownloadRequest, IProgress<DownloadProgress>, CancellationToken, Task<DownloadResult>> download)
        {
            var service = new DownloadQueueService(new FakeDownloadService(download), Store, new ActiveDownloadCoordinator());
            await service.InitializeAsync(CancellationToken.None);
            return service;
        }

        public void Dispose()
        {
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (true)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    return;
                }
                catch (IOException) when (DateTime.UtcNow < timeout)
                {
                    Thread.Sleep(10);
                }
                catch (UnauthorizedAccessException) when (DateTime.UtcNow < timeout)
                {
                    Thread.Sleep(10);
                }
            }
        }
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
}
