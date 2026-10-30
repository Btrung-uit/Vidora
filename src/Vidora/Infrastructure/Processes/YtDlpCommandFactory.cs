namespace Vidora.Infrastructure.Processes;

public static class YtDlpCommandFactory
{
    public static ProcessSpec BuildDiscoverySpec(string ytDlpPath, string url) => new(
        ytDlpPath,
        ["--ignore-config", "--dump-single-json", "--skip-download", "--no-playlist", "--", url]);
}
