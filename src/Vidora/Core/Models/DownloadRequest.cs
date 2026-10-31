namespace Vidora.Core.Models;

public sealed record DownloadRequest(
    Guid Id,
    Uri SourceUrl,
    VideoMetadata Metadata,
    QualityPreset Quality,
    string DestinationDirectory,
    DateTimeOffset CreatedAt);
