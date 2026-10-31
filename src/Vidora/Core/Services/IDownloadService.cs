using Vidora.Core.Models;

namespace Vidora.Core.Services;

public interface IDownloadService
{
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken);
}
