using System.Diagnostics;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class ProcessRunnerCancellationTests
{
    [TestMethod]
    public async Task RunAsync_Cancellation_KillsEntireProcessTreeWithinFiveSeconds()
    {
        var childReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new ImmediateProgress<ProcessOutputLine>(line =>
        {
            const string prefix = "VIDORA_CHILD:";
            if (line.Text.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(line.Text[prefix.Length..], out var processId))
            {
                childReady.TrySetResult(processId);
            }
        });
        var spec = new ProcessSpec(
            "powershell.exe",
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "$child = Start-Process -FilePath $env:ComSpec -ArgumentList '/d','/c','ping -n 31 127.0.0.1 > nul' -WindowStyle Hidden -PassThru; " +
                "Write-Output ('VIDORA_CHILD:' + $child.Id); Wait-Process -Id $child.Id"
            ]);
        using var cancellation = new CancellationTokenSource();
        var run = new ProcessRunner().RunAsync(spec, output, cancellation.Token);
        var childId = await childReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var child = Process.GetProcessById(childId);

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(5)));
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(child.HasExited);
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
