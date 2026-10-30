namespace Vidora.Core.Models;

public sealed record VideoMetadata(
    Uri SourceUrl,
    VideoPlatform Platform,
    string MediaId,
    string Title,
    string? Author,
    TimeSpan? Duration,
    Uri? ThumbnailUrl,
    IReadOnlyList<VideoFormat> Formats);
