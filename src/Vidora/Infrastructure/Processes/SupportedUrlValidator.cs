using Vidora.Core.Errors;

namespace Vidora.Infrastructure.Processes;

public static class SupportedUrlValidator
{
    public static Uri Validate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw InvalidUrl("URL must be an absolute HTTP or HTTPS address.");
        }

        var host = uri.IdnHost.ToLowerInvariant();
        var isSupportedHost = host switch
        {
            "youtube.com" or "www.youtube.com" or "m.youtube.com" => IsYouTubeVideo(uri),
            "youtu.be" or "www.youtu.be" => HasSinglePathSegment(uri),
            "facebook.com" or "www.facebook.com" or "m.facebook.com" => IsFacebookVideo(uri),
            "fb.watch" or "www.fb.watch" => HasSinglePathSegment(uri),
            "instagram.com" or "www.instagram.com" => HasTypedMediaPath(uri, "p", "reel", "tv"),
            "tiktok.com" or "www.tiktok.com" => IsTikTokVideo(uri),
            "vm.tiktok.com" => HasSinglePathSegment(uri),
            _ => throw UnsupportedSource(host)
        };

        return isSupportedHost
            ? uri
            : throw InvalidUrl("The URL does not identify a single public video.");
    }

    private static bool IsYouTubeVideo(Uri uri) =>
        (uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase) && HasQueryValue(uri, "v")) ||
        HasTypedMediaPath(uri, "shorts");

    private static bool IsFacebookVideo(Uri uri) =>
        (uri.AbsolutePath.TrimEnd('/').Equals("/watch", StringComparison.OrdinalIgnoreCase) && HasQueryValue(uri, "v")) ||
        HasTypedMediaPath(uri, "reel") ||
        HasSegmentFollowedByValue(uri, "videos");

    private static bool IsTikTokVideo(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 3 &&
               segments[0].StartsWith('@') &&
               segments[1].Equals("video", StringComparison.OrdinalIgnoreCase) &&
               segments[2].Length > 0;
    }

    private static bool HasTypedMediaPath(Uri uri, params string[] types)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 &&
               types.Contains(segments[0], StringComparer.OrdinalIgnoreCase) &&
               segments[1].Length > 0;
    }

    private static bool HasSinglePathSegment(Uri uri) =>
        uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 1;

    private static bool HasSegmentFollowedByValue(Uri uri, string segmentName)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindIndex(
            segments,
            segment => segment.Equals(segmentName, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < segments.Length && segments[index + 1].Length > 0;
    }

    private static bool HasQueryValue(Uri uri, string name) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Any(parts => parts.Length == 2 &&
                          parts[0].Equals(name, StringComparison.OrdinalIgnoreCase) &&
                          parts[1].Length > 0);

    private static VidoraException InvalidUrl(string detail) => new(new VidoraError(
        VidoraErrorCode.InvalidUrl,
        "Error.InvalidUrl",
        "Action.CheckUrl",
        detail,
        false));

    private static VidoraException UnsupportedSource(string host) => new(new VidoraError(
        VidoraErrorCode.UnsupportedSource,
        "Error.UnsupportedSource",
        "Action.UseSupportedSource",
        $"Unsupported host: {host}",
        false));
}
