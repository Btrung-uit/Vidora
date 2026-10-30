namespace Vidora.Infrastructure.Processes;

public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments);
