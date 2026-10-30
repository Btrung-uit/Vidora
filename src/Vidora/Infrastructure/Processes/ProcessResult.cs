namespace Vidora.Infrastructure.Processes;

public sealed record ProcessResult(
    int ExitCode,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError);
