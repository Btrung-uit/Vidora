using Vidora.Core.Models;

namespace Vidora.Infrastructure.Persistence;

public sealed record QueueDocument(
    int SchemaVersion,
    IReadOnlyList<DownloadQueueItem> Items) : VersionedDocument(SchemaVersion);
