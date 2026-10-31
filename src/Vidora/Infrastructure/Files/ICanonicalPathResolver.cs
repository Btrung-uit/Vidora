namespace Vidora.Infrastructure.Files;

public interface ICanonicalPathResolver
{
    string Resolve(string path);
}
