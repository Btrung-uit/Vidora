namespace Vidora.Core.Models;

public sealed record DownloadProgress(
    double? Percent,
    long? DownloadedBytes,
    long? TotalBytes,
    double? BytesPerSecond,
    TimeSpan? Eta,
    string Stage);
