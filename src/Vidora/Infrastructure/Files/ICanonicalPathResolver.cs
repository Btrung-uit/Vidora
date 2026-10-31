namespace Vidora.Infrastructure.Files;

public interface ICanonicalPathResolver
{
    string Resolve(string path);

    ICanonicalPathLease Acquire(string path);
}

public interface ICanonicalPathLease : IDisposable
{
    string Path { get; }
}
