using System.ComponentModel;
using System.IO;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Processes;

namespace Vidora.Infrastructure.Dependencies;

public sealed class DependencyService(
    IProcessRunner processRunner,
    AppToolPaths toolPaths) : IDependencyService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    public async Task<DependencyReport> CheckAsync(CancellationToken cancellationToken)
    {
        var ytDlpTask = CheckComponentAsync(
            toolPaths.YtDlpPath,
            ["--version"],
            ParseYtDlpVersion,
            cancellationToken);
        var ffmpegTask = CheckComponentAsync(
            toolPaths.FfmpegPath,
            ["-version"],
            ParseFfmpegVersion,
            cancellationToken);

        await Task.WhenAll(ytDlpTask, ffmpegTask);
        return new DependencyReport(await ytDlpTask, await ffmpegTask);
    }

    private async Task<DependencyComponentReport> CheckComponentAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Func<IReadOnlyList<string>, string?> parseVersion,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath))
        {
            return new DependencyComponentReport(DependencyStatus.Missing, null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);

        try
        {
            var result = await processRunner.RunAsync(
                new ProcessSpec(executablePath, arguments),
                null,
                timeout.Token);
            var version = parseVersion(result.StandardOutput);

            return result.ExitCode == 0 && version is not null
                ? new DependencyComponentReport(DependencyStatus.Ready, version)
                : new DependencyComponentReport(DependencyStatus.Invalid, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DependencyComponentReport(DependencyStatus.Invalid, null);
        }
        catch (Win32Exception)
        {
            return new DependencyComponentReport(DependencyStatus.Invalid, null);
        }
        catch (InvalidOperationException)
        {
            return new DependencyComponentReport(DependencyStatus.Invalid, null);
        }
    }

    private static string? ParseYtDlpVersion(IReadOnlyList<string> output) =>
        output.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim();

    private static string? ParseFfmpegVersion(IReadOnlyList<string> output)
    {
        const string prefix = "ffmpeg version ";
        var line = output.FirstOrDefault(line =>
            line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return line?[prefix.Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }
}
