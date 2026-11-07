using System.Diagnostics;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class ProcessRunnerCancellationTests
{
    [TestMethod]
    public async Task RunAsync_Cancellation_KillsEntireProcessTreeWithinFiveSeconds()
    {
        var directory = Directory.CreateTempSubdirectory("vidora-cancel-test-");
        var survivedMarker = Path.Combine(directory.FullName, "survived.txt");
        var childScript = Path.Combine(directory.FullName, "child.cmd");
        var parentScript = Path.Combine(directory.FullName, "parent.cmd");
        await File.WriteAllTextAsync(
            childScript,
            "@echo off\r\n" +
            "echo VIDORA_CHILD_READY\r\n" +
            "ping.exe -n 3 127.0.0.1 >nul\r\n" +
            $"echo survived>\"{survivedMarker}\"\r\n");
        await File.WriteAllTextAsync(
            parentScript,
            "@echo off\r\n" +
            $"cmd.exe /d /s /c \"\"{childScript}\"\"\r\n");
        var childReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new ImmediateProgress<ProcessOutputLine>(line =>
        {
            if (line.Text == "VIDORA_CHILD_READY")
            {
                childReady.TrySetResult();
            }
        });
        var spec = new ProcessSpec(
            Environment.GetEnvironmentVariable("ComSpec")!,
            [
                "/d",
                "/s",
                "/c",
                parentScript
            ]);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var run = new ProcessRunner().RunAsync(spec, output, cancellation.Token);
            await childReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var stopwatch = Stopwatch.StartNew();

            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => run.WaitAsync(TimeSpan.FromSeconds(5)));
            stopwatch.Stop();
            await Task.Delay(TimeSpan.FromSeconds(2.5));

            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
            Assert.IsFalse(File.Exists(survivedMarker), "A descendant continued after process-tree cancellation.");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
