using System.Diagnostics;
using System.IO;

namespace Vidora.Infrastructure.Processes;

public sealed class ProcessRunner : IProcessRunner
{
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
            output);
        var standardErrorTask = ReadLinesAsync(
            process.StandardError,
            ProcessOutputStream.StandardError,
            output);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TerminateAsync(process);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw;
        }

        await Task.WhenAll(standardOutputTask, standardErrorTask);
        return new ProcessResult(
            process.ExitCode,
            standardOutputTask.Result,
            standardErrorTask.Result);
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
        IProgress<ProcessOutputLine>? output)
    {
        var lines = new List<string>();
        while (await reader.ReadLineAsync() is { } line)
        {
            lines.Add(line);
            output?.Report(new ProcessOutputLine(line, stream));
        }

        return lines;
    }

    private static async Task TerminateAsync(Process process)
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
            return;
        }

        await process.WaitForExitAsync(CancellationToken.None);
    }
}
