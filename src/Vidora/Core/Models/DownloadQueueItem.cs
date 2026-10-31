using Vidora.Core.Errors;

namespace Vidora.Core.Models;

public sealed record DownloadQueueItem(
    DownloadRequest Request,
    DownloadStatus Status,
    DownloadProgress? Progress,
    string? FinalPath,
    VidoraError? Error,
    int AttemptCount,
    bool HasDuplicateUrlWarning);
