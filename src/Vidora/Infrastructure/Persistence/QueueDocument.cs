using Vidora.Core.Models;

namespace Vidora.Infrastructure.Persistence;

public sealed record QueueDocument(
    int SchemaVersion,
    IReadOnlyList<DownloadQueueItem> Items) : VersionedDocument(SchemaVersion)
{
    public override bool HasValidShape =>
        Items is not null && Items.All(static item => item is not null);
}
