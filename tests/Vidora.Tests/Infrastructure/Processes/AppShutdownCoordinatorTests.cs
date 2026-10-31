using System.Diagnostics;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class AppShutdownCoordinatorTests
{
    [TestMethod]
    public async Task RequestShutdownAsync_NoActiveDownload_ExitsWithoutPrompt()
    {
        var prompted = false;
        var coordinator = new AppShutdownCoordinator(
            new ActiveDownloadCoordinator(),
            () =>
            {
                prompted = true;
                return AppShutdownDecision.ContinueDownloading;
            },
            (_, _, _) => throw new AssertFailedException("Nothing should be persisted."),
            TimeSpan.FromSeconds(1));

        var outcome = await coordinator.RequestShutdownAsync(CancellationToken.None);

        Assert.AreEqual(AppShutdownOutcome.ExitCompleted, outcome);
        Assert.IsFalse(prompted);
    }

    [TestMethod]
    public async Task RequestShutdownAsync_ContinueDownloading_LeavesActiveTokenUntouched()
    {
        var active = new ActiveDownloadCoordinator();
        var taskId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var activeToken = active.Begin(taskId, CancellationToken.None);
        var coordinator = new AppShutdownCoordinator(
            active,
            () => AppShutdownDecision.ContinueDownloading,
            (_, _, _) => throw new AssertFailedException("Continue must not persist interruption."),
            TimeSpan.FromSeconds(1));

        var outcome = await coordinator.RequestShutdownAsync(CancellationToken.None);

        Assert.AreEqual(AppShutdownOutcome.ContinueRunning, outcome);
        Assert.IsFalse(activeToken.IsCancellationRequested);
        Assert.IsTrue(active.HasActiveDownload);
        active.Complete(taskId);
    }

    [TestMethod]
    public async Task RequestShutdownAsync_CancelAndExit_CancelsWaitsAndPersistsInterrupted()
    {
        var active = new ActiveDownloadCoordinator();
        var taskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var activeToken = active.Begin(taskId, CancellationToken.None);
        activeToken.Register(() => active.Complete(taskId));
        Guid? persistedTaskId = null;
        DownloadResult? persistedResult = null;
        var coordinator = new AppShutdownCoordinator(
            active,
            () => AppShutdownDecision.CancelDownloadAndExit,
            (id, result, _) =>
            {
                persistedTaskId = id;
                persistedResult = result;
                return Task.CompletedTask;
            },
            TimeSpan.FromSeconds(1));

        var outcome = await coordinator.RequestShutdownAsync(CancellationToken.None);

        Assert.AreEqual(AppShutdownOutcome.ExitCompleted, outcome);
        Assert.IsTrue(activeToken.IsCancellationRequested);
        Assert.IsFalse(active.HasActiveDownload);
        Assert.AreEqual(taskId, persistedTaskId);
        Assert.AreEqual(DownloadStatus.Interrupted, persistedResult?.Status);
    }

    [TestMethod]
    public async Task RequestShutdownAsync_CleanupDoesNotComplete_TimesOutAndStillExits()
    {
        var active = new ActiveDownloadCoordinator();
        var taskId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var activeToken = active.Begin(taskId, CancellationToken.None);
        var persisted = false;
        var coordinator = new AppShutdownCoordinator(
            active,
            () => AppShutdownDecision.CancelDownloadAndExit,
            (_, result, _) =>
            {
                persisted = result.Status == DownloadStatus.Interrupted;
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(50));
        var stopwatch = Stopwatch.StartNew();

        var outcome = await coordinator.RequestShutdownAsync(CancellationToken.None);

        stopwatch.Stop();
        Assert.AreEqual(AppShutdownOutcome.ExitCompleted, outcome);
        Assert.IsTrue(activeToken.IsCancellationRequested);
        Assert.IsTrue(persisted);
        Assert.IsLessThan(TimeSpan.FromSeconds(1), stopwatch.Elapsed);
        active.Complete(taskId);
    }

    [TestMethod]
    public async Task RequestShutdownAsync_PersistenceDoesNotComplete_TimesOutAndStillExits()
    {
        var active = new ActiveDownloadCoordinator();
        var taskId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var activeToken = active.Begin(taskId, CancellationToken.None);
        activeToken.Register(() => active.Complete(taskId));
        var coordinator = new AppShutdownCoordinator(
            active,
            () => AppShutdownDecision.CancelDownloadAndExit,
            (_, _, _) => Task.Delay(Timeout.InfiniteTimeSpan),
            TimeSpan.FromMilliseconds(50));
        var stopwatch = Stopwatch.StartNew();

        var outcome = await coordinator.RequestShutdownAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        stopwatch.Stop();
        Assert.AreEqual(AppShutdownOutcome.ExitCompleted, outcome);
        Assert.IsLessThan(TimeSpan.FromSeconds(1), stopwatch.Elapsed);
    }

    [TestMethod]
    public async Task RequestShutdownAsync_DownloadCompletesDuringPrompt_DoesNotPersistInterrupted()
    {
        var active = new ActiveDownloadCoordinator();
        var taskId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        active.Begin(taskId, CancellationToken.None);
        var persisted = false;
        var coordinator = new AppShutdownCoordinator(
            active,
            () =>
            {
                active.Complete(taskId);
                return AppShutdownDecision.CancelDownloadAndExit;
            },
            (_, _, _) =>
            {
                persisted = true;
                return Task.CompletedTask;
            },
            TimeSpan.FromSeconds(1));

        var outcome = await coordinator.RequestShutdownAsync(CancellationToken.None);

        Assert.AreEqual(AppShutdownOutcome.ExitCompleted, outcome);
        Assert.IsFalse(persisted);
    }
}
