using Vidora.Core.Models;

namespace Vidora.Core.Services;

public interface IDependencyService
{
    Task<DependencyReport> CheckAsync(CancellationToken cancellationToken);
}
