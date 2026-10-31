using Vidora.Core.Models;

namespace Vidora.Infrastructure.Persistence;

public sealed record SettingsDocument(
    int SchemaVersion,
    AppSettings Settings) : VersionedDocument(SchemaVersion)
{
    public override bool HasValidShape => Settings is not null;
}
