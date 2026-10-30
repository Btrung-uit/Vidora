namespace Vidora.Core.Models;

public sealed record DependencyReport(
    DependencyComponentReport YtDlp,
    DependencyComponentReport Ffmpeg)
{
    public bool IsReady =>
        YtDlp.Status == DependencyStatus.Ready &&
        Ffmpeg.Status == DependencyStatus.Ready;
}
