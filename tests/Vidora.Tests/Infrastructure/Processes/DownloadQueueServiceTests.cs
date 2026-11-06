using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Persistence;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class DownloadQueueServiceTests
{
    private static readonly DateTimeOffset FinishedAt = new(2026, 11, 1, 10, 30, 0, TimeSpan.Zero);

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
    public async Task RemoveAsync_ActiveAndPending_RemovesOnlyPending()
    {
        using var context = new QueueTestContext();
        var completion = new TaskCompletionSource<DownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = await context.CreateService((_, _, _) => completion.Task);
        var active = Request("44444444-4444-4444-4444-444444444444");
        var pending = Request("55555555-5555-5555-5555-555555555555");
        await service.EnqueueAsync(active, CancellationToken.None);
        await service.EnqueueAsync(pending, CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Any(item => item.Status == DownloadStatus.Downloading));

        var removedActive = await service.RemoveAsync(active.Id, CancellationToken.None);
        var removedPending = await service.RemoveAsync(pending.Id, CancellationToken.None);

        Assert.IsFalse(removedActive);
        Assert.IsTrue(removedPending);
        Assert.IsFalse(service.Items.Any(item => item.Request.Id == pending.Id));
        completion.SetResult(Completed(active));
        await WaitUntilAsync(() => service.Items.Single().Status == DownloadStatus.Completed);
    }

    [TestMethod]
    public async Task RemoveAsync_FailedItem_RemovesPersistedQueueEntryAndKeepsHistory()
    {
        using var context = new QueueTestContext();
        var request = Request("18181818-1818-1818-1818-181818181818");
        await context.Store.SaveAsync(
            [new DownloadQueueItem(request, DownloadStatus.Failed, null, null, Failed().Error, 1, false)],
            CancellationToken.None);
        var service = await context.CreateService((_, _, _) =>
            throw new AssertFailedException("Removing a failed item must not restart it."));
        var changes = 0;
        service.Changed += (_, _) => changes++;

        var removed = await service.RemoveAsync(request.Id, CancellationToken.None);

        Assert.IsTrue(removed);
        Assert.IsEmpty(service.Items);
        Assert.IsEmpty(await context.Store.LoadAsync(CancellationToken.None));
        Assert.AreEqual(request.Id, context.History.Items.Single().TaskId);
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public async Task RemoveAsync_UnrecordedFailure_PreservesItemUntilHistoryCanBeSaved()
    {
        using var context = new QueueTestContext();
        var request = Request("19191919-1919-1919-1919-191919191919");
        await context.Store.SaveAsync(
            [new DownloadQueueItem(request, DownloadStatus.Failed, null, null, Failed().Error, 1, false)],
            CancellationToken.None);
        context.History.FailAdds = true;
        var service = await context.CreateService((_, _, _) =>
            throw new AssertFailedException("Removing a failed item must not restart it."));

        Assert.IsFalse(await service.RemoveAsync(request.Id, CancellationToken.None));
        Assert.HasCount(1, service.Items);
        Assert.HasCount(1, await context.Store.LoadAsync(CancellationToken.None));

        context.History.FailAdds = false;
        Assert.IsTrue(await service.RemoveAsync(request.Id, CancellationToken.None));
        Assert.IsEmpty(service.Items);
        Assert.AreEqual(request.Id, context.History.Items.Single().TaskId);
        Assert.IsFalse(service.HasHistoryPersistenceError);
    }

    [TestMethod]
    public async Task RetryAsync_FailedItem_PreservesHistoryAndRetriesWithNextAttempt()
    {
        using var context = new QueueTestContext();
        var request = Request("20202020-2020-2020-2020-202020202020");
        await context.Store.SaveAsync(
            [new DownloadQueueItem(request, DownloadStatus.Failed, null, null, Failed().Error, 1, false)],
            CancellationToken.None);
        var calls = 0;
        var service = await context.CreateService((retry, _, _) =>
        {
            calls++;
            Assert.AreEqual(request.Id, retry.Id);
            Assert.AreEqual(request.SourceUrl, retry.SourceUrl);
            Assert.AreEqual(request.Quality, retry.Quality);
            Assert.AreEqual(request.DestinationDirectory, retry.DestinationDirectory);
            CollectionAssert.AreEqual(request.Metadata.Formats.ToArray(), retry.Metadata.Formats.ToArray());
            return Task.FromResult(Completed(retry));
        });

        Assert.IsTrue(await service.RetryAsync(request.Id, CancellationToken.None));
        await WaitUntilAsync(() => service.Items.Single().IsHistoryRecorded);

        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, service.Items.Single().AttemptCount);
        Assert.AreEqual(DownloadStatus.Completed, service.Items.Single().Status);
        CollectionAssert.AreEqual(new[] { DownloadStatus.Failed, DownloadStatus.Completed },
            context.History.Items.Select(item => item.Status).ToArray());
        Assert.IsFalse(await service.RetryAsync(request.Id, CancellationToken.None));
        Assert.IsFalse(await service.RetryAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task ClearFinishedAsync_MixedQueue_PreservesPendingAndHistoryAndOutputFile()
    {
        using var context = new QueueTestContext();
        using var directory = new TemporaryDirectory("vidora-clear-queue-");
        var output = Path.Combine(directory.Path, "video.mp4");
        await File.WriteAllTextAsync(output, "keep");
        var statuses = new[] { DownloadStatus.Completed, DownloadStatus.Failed, DownloadStatus.Cancelled, DownloadStatus.Interrupted, DownloadStatus.Pending };
        var items = statuses.Select((status, index) => new DownloadQueueItem(
            Request(new Guid(index + 1, 0, 0, new byte[8]).ToString()), status,
            null, status == DownloadStatus.Completed ? output : null, null, 1, false)).ToArray();
        await context.Store.SaveAsync(items, CancellationToken.None);
        var service = await context.CreateService((_, _, _) =>
            throw new AssertFailedException("Clearing must not resume the queue."));

        Assert.IsTrue(await service.ClearFinishedAsync(CancellationToken.None));

        Assert.AreEqual(DownloadStatus.Pending, service.Items.Single().Status);
        Assert.AreEqual(DownloadStatus.Pending, (await context.Store.LoadAsync(CancellationToken.None)).Single().Status);
        Assert.HasCount(4, context.History.Items);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(output));
        Assert.IsFalse(await service.ClearFinishedAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task RetryAndClear_HistoryUnavailable_KeepUnrecordedFailure()
    {
        using var context = new QueueTestContext();
        var request = Request("21212121-2121-2121-2121-212121212121");
        await context.Store.SaveAsync(
            [new DownloadQueueItem(request, DownloadStatus.Failed, null, null, Failed().Error, 1, false)],
            CancellationToken.None);
        context.History.FailAdds = true;
        var service = await context.CreateService((_, _, _) =>
            throw new AssertFailedException("Unrecorded history must be preserved."));

        Assert.IsFalse(await service.RetryAsync(request.Id, CancellationToken.None));
        Assert.IsFalse(await service.ClearFinishedAsync(CancellationToken.None));
        Assert.AreEqual(DownloadStatus.Failed, service.Items.Single().Status);
        Assert.HasCount(1, await context.Store.LoadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ClearFinishedAsync_ActiveDownload_DoesNotCancelOrRemoveIt()
    {
        using var context = new QueueTestContext();
        var first = Request("22222222-1111-1111-1111-111111111111");
        var active = Request("23232323-1111-1111-1111-111111111111");
        await context.Store.SaveAsync([
            new DownloadQueueItem(first, DownloadStatus.Failed, null, null, Failed().Error, 1, false),
            new DownloadQueueItem(active, DownloadStatus.Pending, null, null, null, 0, false)
        ], CancellationToken.None);
        var completion = new TaskCompletionSource<DownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = await context.CreateService((_, _, token) => completion.Task.WaitAsync(token));
        await service.ResumeAsync(CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Any(item => item.Status == DownloadStatus.Downloading));

        Assert.IsTrue(await service.ClearFinishedAsync(CancellationToken.None));
        Assert.AreEqual(active.Id, service.Items.Single().Request.Id);
        Assert.AreEqual(DownloadStatus.Downloading, service.Items.Single().Status);
        completion.SetResult(Completed(active));
        await WaitUntilAsync(() => service.Items.Single().IsHistoryRecorded);
        Assert.AreEqual(DownloadStatus.Completed, service.Items.Single().Status);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RetryAndClear_QueueSaveFails_KeepPublishedState(bool retry)
    {
        var request = Request("24242424-1111-1111-1111-111111111111");
        var failed = new DownloadQueueItem(request, DownloadStatus.Failed, null, null, Failed().Error, 1, false, true);
        var store = new FailingQueueStore(1, [failed]);
        var service = new DownloadQueueService(new FakeDownloadService((_, _, _) =>
            throw new AssertFailedException("Failed mutation must not start a download.")),
            store, new ActiveDownloadCoordinator(), new RecordingHistoryService(), TimeProvider.System);
        await service.InitializeAsync(CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            if (retry) await service.RetryAsync(request.Id, CancellationToken.None);
            else await service.ClearFinishedAsync(CancellationToken.None);
        });
        Assert.AreEqual(failed, service.Items.Single());
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
    public async Task EnqueueAsync_UrlCompletedInHistory_MarksDuplicateWarning()
    {
        using var context = new QueueTestContext();
        var priorRequest = Request("14141414-1414-1414-1414-141414141414");
        await context.History.AddAsync(
            HistoryItem(priorRequest, DownloadStatus.Completed),
            CancellationToken.None);
        var completion = new TaskCompletionSource<DownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = await context.CreateService((_, _, _) => completion.Task);
        var request = Request("15151515-1515-1515-1515-151515151515") with
        {
            SourceUrl = priorRequest.SourceUrl,
            Metadata = priorRequest.Metadata with { SourceUrl = priorRequest.SourceUrl }
        };

        await service.EnqueueAsync(request, CancellationToken.None);

        Assert.IsTrue(service.Items.Single().HasDuplicateUrlWarning);
        completion.SetResult(Completed(request));
        await WaitUntilAsync(() => service.Items.Single().Status == DownloadStatus.Completed);
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
        var history = context.History.Items.Single();
        Assert.AreEqual(DownloadStatus.Interrupted, history.Status);
        Assert.AreEqual(request.Id, history.TaskId);
        Assert.AreEqual(1, history.AttemptCount);
        Assert.AreEqual(FinishedAt, history.Timestamp);
    }

    [TestMethod]
    [DataRow(DownloadStatus.Completed)]
    [DataRow(DownloadStatus.Failed)]
    [DataRow(DownloadStatus.Cancelled)]
    public async Task TerminalDownload_AddsOneHistorySnapshot(DownloadStatus status)
    {
        using var context = new QueueTestContext();
        var request = Request("13131313-1313-1313-1313-131313131313");
        var error = status == DownloadStatus.Completed
            ? null
            : new VidoraError(VidoraErrorCode.Unknown, "Error.DownloadFailed", "Action.TryAgain", "test", true);
        var result = new DownloadResult(
            status,
            status == DownloadStatus.Completed ? Path.Combine(request.DestinationDirectory, "video.mp4") : null,
            error);
        var service = await context.CreateService((_, _, _) => Task.FromResult(result));

        await service.EnqueueAsync(request, CancellationToken.None);
        await WaitUntilAsync(() => context.History.Items.Count == 1);

        var item = context.History.Items.Single();
        Assert.AreEqual(request.Id, item.TaskId);
        Assert.AreEqual(1, item.AttemptCount);
        Assert.AreEqual(FinishedAt, item.Timestamp);
        Assert.AreEqual(request.SourceUrl, item.SourceUrl);
        Assert.AreEqual(request.Metadata.Title, item.Title);
        Assert.AreEqual(request.Metadata.Platform, item.Platform);
        Assert.AreEqual(request.Quality, item.Quality);
        Assert.AreEqual(request.DestinationDirectory, item.DestinationDirectory);
        Assert.AreEqual(status, item.Status);
        Assert.AreEqual(result.FinalPath, item.FinalPath);
        Assert.AreEqual(error?.Code, item.ErrorCode);
    }

    [TestMethod]
    public async Task ProcessingSaveFails_StopsQueueAndSurfacesDiskFailure()
    {
        var store = new FailingQueueStore(failOnSaveCall: 2);
        var downloadCalled = false;
        var activeDownloads = new ActiveDownloadCoordinator();
        var service = new DownloadQueueService(
            new FakeDownloadService((request, _, _) =>
            {
                downloadCalled = true;
                return Task.FromResult(Completed(request));
            }),
            store,
            activeDownloads,
            new RecordingHistoryService(),
            new FixedTimeProvider(FinishedAt));
        await service.InitializeAsync(CancellationToken.None);

        await service.EnqueueAsync(
            Request("12121212-1212-1212-1212-121212121212"),
            CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Single().Status == DownloadStatus.Failed);

        var failed = service.Items.Single();
        Assert.AreEqual(VidoraErrorCode.DiskFailure, failed.Error?.Code);
        Assert.IsFalse(downloadCalled);
        Assert.IsFalse(activeDownloads.HasActiveDownload);
    }

    [TestMethod]
    public async Task HistorySaveFails_CompletedDownloadStaysCompletedAndResumeReconcilesSnapshot()
    {
        using var context = new QueueTestContext();
        context.History.FailAdds = true;
        var request = Request("16161616-1616-1616-1616-161616161616");
        var service = await context.CreateService((_, _, _) => Task.FromResult(Completed(request)));

        await service.EnqueueAsync(request, CancellationToken.None);
        await WaitUntilAsync(() => context.History.AddCalls == 1);

        var completed = service.Items.Single();
        Assert.AreEqual(DownloadStatus.Completed, completed.Status);
        Assert.IsNotNull(completed.FinalPath);
        Assert.IsFalse(completed.IsHistoryRecorded);
        Assert.IsTrue(service.HasHistoryPersistenceError);

        context.History.FailAdds = false;
        await service.ResumeAsync(CancellationToken.None);
        await WaitUntilAsync(() => service.Items.Single().IsHistoryRecorded);

        Assert.AreEqual(DownloadStatus.Completed, service.Items.Single().Status);
        Assert.IsFalse(service.HasHistoryPersistenceError);
        Assert.HasCount(1, context.History.Items);
    }

    [TestMethod]
    public async Task InitializeAsync_CrashRestoredDownload_AddsInterruptedHistoryOnlyOnce()
    {
        using var directory = new TemporaryDirectory("vidora-history-recovery-test-");
        var queuePath = Path.Combine(directory.Path, "queue.json");
        var historyPath = Path.Combine(directory.Path, "history.json");
        var request = Request("17171717-1717-1717-1717-171717171717");
        var queueStore = new QueueStore(queuePath, TimeProvider.System);
        await queueStore.SaveAsync(
            [new DownloadQueueItem(request, DownloadStatus.Downloading, null, null, null, 1, false)],
            CancellationToken.None);
        var firstTime = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var history = new HistoryService(historyPath, TimeProvider.System);
        var first = new DownloadQueueService(
            new FakeDownloadService((_, _, _) => throw new AssertFailedException("Restored queue must stay stopped.")),
            new QueueStore(queuePath, TimeProvider.System),
            new ActiveDownloadCoordinator(),
            history,
            new FixedTimeProvider(firstTime));

        await first.InitializeAsync(CancellationToken.None);

        var firstHistory = await history.LoadAsync(CancellationToken.None);
        Assert.HasCount(1, firstHistory);
        Assert.AreEqual(DownloadStatus.Interrupted, firstHistory[0].Status);
        Assert.AreEqual(firstTime, firstHistory[0].Timestamp);
        Assert.IsTrue(first.Items.Single().IsHistoryRecorded);

        var second = new DownloadQueueService(
            new FakeDownloadService((_, _, _) => throw new AssertFailedException("Restored queue must stay stopped.")),
            new QueueStore(queuePath, TimeProvider.System),
            new ActiveDownloadCoordinator(),
            history,
            new FixedTimeProvider(firstTime.AddHours(1)));
        await second.InitializeAsync(CancellationToken.None);

        var secondHistory = await history.LoadAsync(CancellationToken.None);
        Assert.HasCount(1, secondHistory);
        Assert.AreEqual(firstTime, secondHistory[0].Timestamp);
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

    private static DownloadHistoryItem HistoryItem(DownloadRequest request, DownloadStatus status) => new(
        request.Id,
        1,
        FinishedAt,
        request.SourceUrl,
        request.Metadata.MediaId,
        request.Metadata.Title,
        request.Metadata.Platform,
        request.Metadata.Formats,
        request.Quality,
        request.DestinationDirectory,
        status,
        null,
        null);

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

        public RecordingHistoryService History { get; } = new();

        public async Task<DownloadQueueService> CreateService(
            Func<DownloadRequest, IProgress<DownloadProgress>, CancellationToken, Task<DownloadResult>> download)
        {
            var service = new DownloadQueueService(
                new FakeDownloadService(download),
                Store,
                new ActiveDownloadCoordinator(),
                History,
                new FixedTimeProvider(FinishedAt));
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

    private sealed class FailingQueueStore(int failOnSaveCall, IReadOnlyList<DownloadQueueItem>? initialItems = null) : IQueueStore
    {
        private int saveCalls;

        public Task<IReadOnlyList<DownloadQueueItem>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownloadQueueItem>>(initialItems ?? []);

        public Task SaveAsync(IReadOnlyList<DownloadQueueItem> items, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref saveCalls) == failOnSaveCall)
            {
                throw new IOException("Disk full for test.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHistoryService : IHistoryService
    {
        private readonly object sync = new();
        private readonly List<DownloadHistoryItem> items = [];

        public bool FailAdds { get; set; }

        public int AddCalls { get; private set; }

        public IReadOnlyList<DownloadHistoryItem> Items
        {
            get
            {
                lock (sync)
                {
                    return items.ToArray();
                }
            }
        }

        public Task<IReadOnlyList<DownloadHistoryItem>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Items);

        public Task AddAsync(DownloadHistoryItem item, CancellationToken cancellationToken)
        {
            AddCalls++;
            if (FailAdds)
            {
                throw new IOException("History disk full for test.");
            }

            lock (sync)
            {
                items.RemoveAll(existing =>
                    existing.TaskId == item.TaskId && existing.AttemptCount == item.AttemptCount);
                items.Add(item);
            }

            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            lock (sync)
            {
                items.Clear();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TemporaryDirectory(string prefix) : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory(prefix).FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
