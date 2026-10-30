using System.Collections.Concurrent;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class ProcessRunnerTests
{
    [TestMethod]
    public async Task RunAsync_CapturesStdoutAndStderrWithoutDeadlock()
    {
        var reported = new ConcurrentQueue<ProcessOutputLine>();
        var progress = new InlineProgress<ProcessOutputLine>(reported.Enqueue);
        var spec = Cmd("echo stdout-line& echo stderr-line>&2");

        var result = await new ProcessRunner().RunAsync(spec, progress, CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        CollectionAssert.Contains(result.StandardOutput.ToList(), "stdout-line");
        CollectionAssert.Contains(result.StandardError.ToList(), "stderr-line");
        Assert.IsTrue(reported.Any(line => line is { Text: "stdout-line", Stream: ProcessOutputStream.StandardOutput }));
        Assert.IsTrue(reported.Any(line => line is { Text: "stderr-line", Stream: ProcessOutputStream.StandardError }));
    }

    [TestMethod]
    public async Task RunAsync_PreservesArgumentsContainingSpacesAndAmpersands()
    {
        var directory = Directory.CreateTempSubdirectory("vidora-process-test-");
        var scriptPath = Path.Combine(directory.FullName, "print-argument.cmd");
        const string argument = "value with spaces & ampersand";

        try
        {
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\nsetlocal EnableDelayedExpansion\r\nset \"argument=%~1\"\r\necho(!argument!\r\n");
            var spec = new ProcessSpec(
                Environment.GetEnvironmentVariable("ComSpec")!,
                ["/d", "/s", "/c", scriptPath, argument]);

            var result = await new ProcessRunner().RunAsync(spec, null, CancellationToken.None);

            Assert.AreEqual(0, result.ExitCode, string.Join(Environment.NewLine, result.StandardError));
            CollectionAssert.AreEqual(new[] { argument }, result.StandardOutput.ToArray());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task RunAsync_NonZeroExitCode_IsReturnedWithoutThrowing()
    {
        var result = await new ProcessRunner().RunAsync(Cmd("exit /b 7"), null, CancellationToken.None);

        Assert.AreEqual(7, result.ExitCode);
    }

    [TestMethod]
    public async Task RunAsync_AlreadyCancelled_DoesNotStartProcess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var spec = new ProcessSpec("vidora-executable-that-does-not-exist.exe", []);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new ProcessRunner().RunAsync(spec, null, cancellation.Token));
    }

    private static ProcessSpec Cmd(string command) => new(
        Environment.GetEnvironmentVariable("ComSpec")!,
        ["/d", "/s", "/c", command]);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
