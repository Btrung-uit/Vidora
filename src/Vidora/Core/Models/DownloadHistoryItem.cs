using Vidora.Core.Errors;

namespace Vidora.Core.Models;

public sealed record DownloadHistoryItem(
    Guid TaskId,
    int AttemptCount,
    DateTimeOffset Timestamp,
    Uri SourceUrl,
    string MediaId,
    string Title,
    VideoPlatform Platform,
    IReadOnlyList<VideoFormat> Formats,
    QualityPreset Quality,
    string DestinationDirectory,
    DownloadStatus Status,
    string? FinalPath,
    VidoraErrorCode? ErrorCode);
