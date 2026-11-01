using System.IO;

namespace Vidora.Infrastructure.Dependencies;

public interface IYtDlpReleaseClient
{
    Task<YtDlpReleaseInfo> GetLatestAsync(CancellationToken cancellationToken);

    Task DownloadAssetAsync(
        Uri assetUri,
        Stream destination,
        CancellationToken cancellationToken);
}
