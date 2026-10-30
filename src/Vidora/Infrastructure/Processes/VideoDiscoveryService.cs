using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Dependencies;

namespace Vidora.Infrastructure.Processes;

public sealed class VideoDiscoveryService(
    IProcessRunner processRunner,
    IDependencyService dependencyService,
    AppToolPaths toolPaths) : IVideoDiscoveryService
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(30);

    public async Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken)
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
            throw new VidoraException(new VidoraError(
                VidoraErrorCode.NetworkFailure,
                "Error.DiscoveryTimeout",
                "Action.TryAgain",
                "Metadata discovery exceeded the 30-second timeout.",
                true));
        }

        if (result.ExitCode != 0)
        {
            throw new VidoraException(new VidoraError(
                VidoraErrorCode.Unknown,
                "Error.DiscoveryFailed",
                "Action.TryAgain",
                $"yt-dlp exited with code {result.ExitCode}.",
                true));
        }

        return YtDlpDiscoveryParser.Parse(string.Join(Environment.NewLine, result.StandardOutput), url);
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
