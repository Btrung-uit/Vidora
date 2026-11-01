using System.ComponentModel;
using System.IO;
using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Logging;

namespace Vidora.Infrastructure.Processes;

public sealed class VideoDiscoveryService(
    IProcessRunner processRunner,
    IDependencyService dependencyService,
    AppToolPaths toolPaths,
    IAppLogger logger) : IVideoDiscoveryService
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(30);

    public async Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        try
        {
            return await DiscoverCoreAsync(url, cancellationToken);
        }
        catch (VidoraException exception)
        {
            await LogAsync(operationId, "DiscoveryFailed", exception.Error);
            throw;
        }
        catch (OperationCanceledException)
        {
            var error = YtDlpErrorClassifier.Classify(
                new ProcessResult(-1, [], []),
                YtDlpOperation.Discovery,
                wasCancelled: true);
            await LogAsync(operationId, "DiscoveryCancelled", error, FileLogLevel.Warning);
            throw;
        }
    }

    private async Task<VideoMetadata> DiscoverCoreAsync(string url, CancellationToken cancellationToken)
    {
        SupportedUrlValidator.Validate(url);
        var dependencies = await dependencyService.CheckAsync(cancellationToken);
        if (!dependencies.IsReady)
        {
            throw DependencyFailure(dependencies);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DiscoveryTimeout);

        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(
                YtDlpCommandFactory.BuildDiscoverySpec(toolPaths.YtDlpPath, url),
                null,
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var error = new VidoraError(
                VidoraErrorCode.NetworkFailure,
                "Error.DiscoveryTimeout",
                "Action.TryAgain",
                "Metadata discovery exceeded the 30-second timeout.",
                true);
            throw new VidoraException(error);
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
            throw new VidoraException(error);
        }

        if (result.ExitCode != 0)
        {
            var error = YtDlpErrorClassifier.Classify(result, YtDlpOperation.Discovery);
            throw new VidoraException(error);
        }

        return YtDlpDiscoveryParser.Parse(string.Join(Environment.NewLine, result.StandardOutput), url);
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
                    nameof(VideoDiscoveryService),
                    eventName,
                    error.Code,
                    DiagnosticRedactor.Redact(error.TechnicalDetail)),
                CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    private static VidoraException DependencyFailure(DependencyReport report)
    {
        var isMissing = report.YtDlp.Status == DependencyStatus.Missing ||
                        report.Ffmpeg.Status == DependencyStatus.Missing;
        return new VidoraException(new VidoraError(
            isMissing ? VidoraErrorCode.DependencyMissing : VidoraErrorCode.DependencyInvalid,
            isMissing ? "Error.DependencyMissing" : "Error.DependencyInvalid",
            "Action.OpenSettings",
            "Required media tools are not ready.",
            false));
    }
}
