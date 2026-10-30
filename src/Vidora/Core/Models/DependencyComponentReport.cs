namespace Vidora.Core.Models;

public sealed record DependencyComponentReport(
    DependencyStatus Status,
    string? Version);
