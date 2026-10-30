namespace Vidora.Infrastructure.Processes;

public enum ProcessOutputStream
{
    StandardOutput,
    StandardError
}

public sealed record ProcessOutputLine(
    string Text,
    ProcessOutputStream Stream);
