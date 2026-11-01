using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Infrastructure.Processes;

namespace Vidora.Infrastructure.Persistence;

public sealed record HistoryDocument(
    int SchemaVersion,
    IReadOnlyList<DownloadHistoryItem> Items) : VersionedDocument(SchemaVersion)
{
    public override bool HasValidShape =>
        Items is not null && Items.All(static item =>
            item is not null &&
            HasSupportedUrl(item.SourceUrl) &&
            !string.IsNullOrWhiteSpace(item.MediaId) &&
            !string.IsNullOrWhiteSpace(item.Title) &&
            item.Formats is not null &&
            item.Formats.All(static format =>
                format is not null &&
                !string.IsNullOrWhiteSpace(format.FormatId) &&
                !string.IsNullOrWhiteSpace(format.Extension)) &&
            !string.IsNullOrWhiteSpace(item.DestinationDirectory));

    private static bool HasSupportedUrl(Uri? uri)
    {
        if (uri?.IsAbsoluteUri != true)
        {
            return false;
        }

        try
        {
            SupportedUrlValidator.Validate(uri.AbsoluteUri);
            return true;
        }
        catch (VidoraException)
        {
            return false;
        }
    }
}
