using System.ComponentModel;
using System.IO;
using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Files;

namespace Vidora.Infrastructure.Processes;

public sealed class DownloadService(
    IProcessRunner processRunner,
    AppToolPaths toolPaths) : IDownloadService
{
    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var destination = DestinationValidator.Validate(request.DestinationDirectory);
        var baseName = OutputNamePolicy.CreateAvailableBaseName(
            destination,
            request.Metadata.Title,
            request.Metadata.Platform,
            request.Metadata.MediaId);
        var outputTemplate = Path.Combine(destination, $"{baseName}.%(ext)s");
        var formatSelector = FormatSelector.Select(request.Metadata.Formats, request.Quality);
        string? finalPath = null;
        var output = new InlineProgress<ProcessOutputLine>(line =>
        {
            if (YtDlpProgressParser.TryParseProgress(line.Text, out var parsedProgress))
            {
                progress.Report(parsedProgress!);
            }

            if (YtDlpProgressParser.TryParseFinalPath(line.Text, out var parsedPath))
            {
                Volatile.Write(ref finalPath, parsedPath);
            }
        });

        ProcessResult processResult;
        try
        {
            processResult = await processRunner.RunAsync(
                YtDlpCommandFactory.BuildDownloadSpec(
                    toolPaths.YtDlpPath,
                    toolPaths.FfmpegPath,
                    formatSelector,
                    outputTemplate,
                    request.SourceUrl.OriginalString),
                output,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or InvalidOperationException)
        {
            throw new VidoraException(new VidoraError(
                VidoraErrorCode.DependencyInvalid,
                "Error.DependencyInvalid",
                "Action.OpenSettings",
                exception.Message,
                false));
        }

        if (processResult.ExitCode != 0)
        {
            throw new VidoraException(new VidoraError(
                VidoraErrorCode.Unknown,
                "Error.DownloadFailed",
                "Action.TryAgain",
                $"yt-dlp exited with code {processResult.ExitCode}.",
                true));
        }

        var reportedPath = Volatile.Read(ref finalPath);
        if (string.IsNullOrWhiteSpace(reportedPath) || !Path.IsPathRooted(reportedPath))
        {
            throw MissingFinalPath();
        }

        var canonicalPath = Path.GetFullPath(reportedPath);
        if (!IsWithin(destination, canonicalPath))
        {
            throw new VidoraException(new VidoraError(
                VidoraErrorCode.DiskFailure,
                "Error.OutputOutsideDestination",
                "Action.ChooseAnotherFolder",
                "yt-dlp reported an output path outside the selected destination.",
                false));
        }

        if (!File.Exists(canonicalPath))
        {
            throw MissingFinalPath();
        }

        return new DownloadResult(DownloadStatus.Completed, canonicalPath, null);
    }

    private static bool IsWithin(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static VidoraException MissingFinalPath() => new(new VidoraError(
        VidoraErrorCode.PostProcessingFailure,
        "Error.FinalOutputMissing",
        "Action.TryAgain",
        "yt-dlp completed without an existing final output path.",
        true));

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
