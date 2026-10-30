using Vidora.Core.Models;

namespace Vidora.Core.Services;

public interface IVideoDiscoveryService
{
    Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken);
}
