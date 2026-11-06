using Vidora.Core.Models;

namespace Vidora.Core.Services;

public interface IDependencyService
{
    Task<DependencyReport> CheckAsync(CancellationToken cancellationToken);

    Task<DependencyComponentReport> CheckFfmpegAsync(string executablePath, CancellationToken cancellationToken);

    Task<string> GetLatestYtDlpVersionAsync(CancellationToken cancellationToken);

    Task<DependencyReport> UpdateYtDlpAsync(CancellationToken cancellationToken);
}
