namespace Vidora.Infrastructure.Processes;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        IProgress<ProcessOutputLine>? output,
        CancellationToken cancellationToken);
}
