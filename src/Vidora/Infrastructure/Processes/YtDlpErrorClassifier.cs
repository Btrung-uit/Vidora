using Vidora.Core.Errors;
using Vidora.Infrastructure.Logging;

namespace Vidora.Infrastructure.Processes;

public enum YtDlpOperation
{
    Discovery,
    Download
}

public static class YtDlpErrorClassifier
{
    public static VidoraError Classify(
        ProcessResult result,
        YtDlpOperation operation,
        bool wasCancelled = false)
    {
        if (wasCancelled)
        {
            return Create(VidoraErrorCode.Cancelled, operation, "Operation was cancelled.");
        }

        var standardError = string.Join(Environment.NewLine, result.StandardError);
        var code = ClassifyCode(standardError);
        var detail = $"yt-dlp exit code {result.ExitCode}.";
        var redactedError = DiagnosticRedactor.Redact(standardError);
        if (!string.IsNullOrWhiteSpace(redactedError))
        {
            detail += Environment.NewLine + redactedError;
        }

        return Create(code, operation, detail);
    }

    private static VidoraErrorCode ClassifyCode(string text)
    {
        if (ContainsAny(text, "no space left on device", "not enough space on the disk", "disk full"))
        {
            return VidoraErrorCode.InsufficientSpace;
        }

        if (ContainsAny(text, "ffmpeg not found", "ffprobe not found", "could not find ffmpeg"))
        {
            return VidoraErrorCode.DependencyMissing;
        }

        if (ContainsAny(
                text,
                "version of yt-dlp is out of date",
                "yt-dlp is out of date",
                "update your yt-dlp"))
        {
            return VidoraErrorCode.ExtractorOutdated;
        }

        if (ContainsAny(text, "ffmpeg exited with code", "ffprobe exited with code", "unable to execute ffmpeg"))
        {
            return VidoraErrorCode.DependencyInvalid;
        }

        if (ContainsAny(
                text,
                "login required",
                "sign in",
                "authentication required",
                "this video is private",
                "use --cookies"))
        {
            return VidoraErrorCode.AuthenticationRequired;
        }

        if (ContainsAny(
                text,
                "video unavailable",
                "content is not available",
                "video has been removed",
                "not available in your country"))
        {
            return VidoraErrorCode.Unavailable;
        }

        if (ContainsAny(
                text,
                "unable to download webpage",
                "timed out",
                "connection reset",
                "temporary failure in name resolution",
                "network is unreachable",
                "http error 429"))
        {
            return VidoraErrorCode.NetworkFailure;
        }

        if (ContainsAny(
                text,
                "permission denied",
                "access is denied",
                "unable to open for writing",
                "read-only file system"))
        {
            return VidoraErrorCode.DiskFailure;
        }

        if (ContainsAny(text, "postprocessing:", "post-processing", "conversion failed"))
        {
            return VidoraErrorCode.PostProcessingFailure;
        }

        if (ContainsAny(text, "not a valid url", "invalid url"))
        {
            return VidoraErrorCode.InvalidUrl;
        }

        return ContainsAny(text, "unsupported url")
            ? VidoraErrorCode.UnsupportedSource
            : VidoraErrorCode.Unknown;
    }

    private static VidoraError Create(
        VidoraErrorCode code,
        YtDlpOperation operation,
        string technicalDetail)
    {
        var (messageKey, actionKey, retryable) = code switch
        {
            VidoraErrorCode.InvalidUrl => ("Error.InvalidUrl", "Action.CheckUrl", false),
            VidoraErrorCode.UnsupportedSource => ("Error.UnsupportedSource", "Action.UseSupportedSource", false),
            VidoraErrorCode.Unavailable => ("Error.Unavailable", "Action.UsePublicVideo", false),
            VidoraErrorCode.AuthenticationRequired => ("Error.AuthenticationRequired", "Action.UsePublicVideo", false),
            VidoraErrorCode.NetworkFailure => ("Error.NetworkFailure", "Action.TryAgain", true),
            VidoraErrorCode.DiskFailure => ("Error.DiskFailure", "Action.ChooseAnotherFolder", false),
            VidoraErrorCode.InsufficientSpace => ("Error.InsufficientSpace", "Action.FreeSpace", false),
            VidoraErrorCode.DependencyMissing => ("Error.DependencyMissing", "Action.OpenSettings", false),
            VidoraErrorCode.DependencyInvalid => ("Error.DependencyInvalid", "Action.OpenSettings", false),
            VidoraErrorCode.ExtractorOutdated => ("Error.ExtractorOutdated", "Action.UpdateYtDlp", true),
            VidoraErrorCode.PostProcessingFailure => ("Error.PostProcessingFailure", "Action.TryAgain", true),
            VidoraErrorCode.Cancelled => ("Error.Cancelled", "Action.TryAgain", true),
            _ => (
                operation == YtDlpOperation.Discovery ? "Error.DiscoveryFailed" : "Error.DownloadFailed",
                "Action.TryAgain",
                true)
        };

        return new VidoraError(code, messageKey, actionKey, technicalDetail, retryable);
    }

    private static bool ContainsAny(string text, params string[] markers) =>
        markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
