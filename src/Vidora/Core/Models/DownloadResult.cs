using Vidora.Core.Errors;

namespace Vidora.Core.Models;

public sealed record DownloadResult(
    DownloadStatus Status,
    string? FinalPath,
    VidoraError? Error);
