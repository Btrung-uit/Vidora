using Vidora.Core.Models;

namespace Vidora.Infrastructure.Persistence;

public sealed record HistoryDocument(
    int SchemaVersion,
    IReadOnlyList<DownloadHistoryItem> Items) : VersionedDocument(SchemaVersion)
{
    public override bool HasValidShape =>
        Items is not null && Items.All(static item =>
            item is not null &&
            item.SourceUrl is not null &&
            item.MediaId is not null &&
            item.Title is not null &&
            item.Formats is not null &&
            item.Formats.All(static format => format is not null) &&
            item.DestinationDirectory is not null);
}
