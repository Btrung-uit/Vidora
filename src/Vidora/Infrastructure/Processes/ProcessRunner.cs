using System.Diagnostics;
using System.IO;

namespace Vidora.Infrastructure.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(2);

    public async Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        IProgress<ProcessOutputLine>? output,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = CreateStartInfo(spec)
        };

        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start process '{spec.FileName}'.");
        }

        var standardOutputTask = ReadLinesAsync(
            process.StandardOutput,
            ProcessOutputStream.StandardOutput,
            output,
            cancellationToken);
        var standardErrorTask = ReadLinesAsync(
            process.StandardError,
            ProcessOutputStream.StandardError,
            output,
            cancellationToken);

        try
        {
            await Task.WhenAll(
                    process.WaitForExitAsync(CancellationToken.None),
                    standardOutputTask,
                    standardErrorTask)
                .WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TerminateAsync(process, standardOutputTask, standardErrorTask);
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static ProcessStartInfo CreateStartInfo(ProcessSpec spec)
    {
        var startInfo = new ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<IReadOnlyList<string>> ReadLinesAsync(
        StreamReader reader,
        ProcessOutputStream stream,
        IProgress<ProcessOutputLine>? output,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
            output?.Report(new ProcessOutputLine(line, stream));
        }

        return lines;
    }

    private static async Task TerminateAsync(
        Process process,
        Task standardOutputTask,
        Task standardErrorTask)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            await WaitForExitWithinTimeoutAsync(process);
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            await DrainWithinTimeoutAsync(standardOutputTask, standardErrorTask);
        }
    }

    private static async Task WaitForExitWithinTimeoutAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TerminationTimeout);
        }
        catch (InvalidOperationException)
        {
        }
        catch (TimeoutException)
        {
        }
    }

    private static async Task DrainWithinTimeoutAsync(params Task[] streamTasks)
    {
        try
        {
            await Task.WhenAll(streamTasks).WaitAsync(TerminationTimeout);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
        }
    }
}
