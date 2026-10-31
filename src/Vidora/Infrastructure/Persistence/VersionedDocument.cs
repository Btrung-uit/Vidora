namespace Vidora.Infrastructure.Persistence;

public abstract record VersionedDocument(int SchemaVersion);

public enum AtomicStoreLoadStatus
{
    Loaded,
    Missing,
    RecoveredCorrupt,
    UnsupportedSchema
}

public sealed record AtomicStoreLoadResult<TDocument>(
    TDocument Document,
    AtomicStoreLoadStatus Status,
    string? QuarantinedPath);
