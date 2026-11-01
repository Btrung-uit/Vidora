using System.ComponentModel;
using System.IO;
using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Files;
using Vidora.Infrastructure.Logging;

namespace Vidora.Infrastructure.Processes;

public sealed class DownloadService(
    IProcessRunner processRunner,
    AppToolPaths toolPaths,
    TaskWorkspaceService taskWorkspaceService,
    IAppLogger logger) : IDownloadService
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
        var outputTemplate = baseName.Replace("%", "%%", StringComparison.Ordinal) + ".%(ext)s";
        var formatSelector = FormatSelector.Select(request.Metadata.Formats, request.Quality);
        string taskDirectory;
        try
        {
            taskDirectory = taskWorkspaceService.Create(request.Id);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var error = new VidoraError(
                VidoraErrorCode.DiskFailure,
                "Error.TempWorkspaceUnavailable",
                "Action.TryAgain",
                DiagnosticRedactor.Redact(exception.Message),
                true);
            await LogAsync(request.Id, "DownloadFailed", error);
            throw new VidoraException(error);
        }

        try
        {
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
                        destination,
                        taskDirectory,
                        request.SourceUrl.OriginalString),
                    output,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                var error = YtDlpErrorClassifier.Classify(
                    new ProcessResult(-1, [], []),
                    YtDlpOperation.Download,
                    wasCancelled: true);
                await LogAsync(request.Id, "DownloadCancelled", error, FileLogLevel.Warning);
                throw;
            }
            catch (Exception exception) when (
                exception is Win32Exception or IOException or InvalidOperationException)
            {
                var error = new VidoraError(
                    VidoraErrorCode.DependencyInvalid,
                    "Error.DependencyInvalid",
                    "Action.OpenSettings",
                    DiagnosticRedactor.Redact(exception.Message),
                    false);
                await LogAsync(request.Id, "DownloadFailed", error);
                throw new VidoraException(error);
            }

            if (processResult.ExitCode != 0)
            {
                var error = YtDlpErrorClassifier.Classify(processResult, YtDlpOperation.Download);
                await LogAsync(request.Id, "DownloadFailed", error);
                throw new VidoraException(error);
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
        finally
        {
            taskWorkspaceService.Cleanup(request.Id);
        }
    }

    private async Task LogAsync(
        Guid operationId,
        string eventName,
        VidoraError error,
        FileLogLevel level = FileLogLevel.Error)
    {
        try
        {
            await logger.WriteAsync(
                new FileLogEntry(
                    level,
                    operationId,
                    nameof(DownloadService),
                    eventName,
                    error.Code,
                    DiagnosticRedactor.Redact(error.TechnicalDetail)),
                CancellationToken.None);
        }
        catch (Exception)
        {
        }
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
