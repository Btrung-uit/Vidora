namespace Vidora.Infrastructure.Processes;

public static class YtDlpCommandFactory
{
    private const string ProgressTemplate =
        "download:VIDORA_PROGRESS:%(progress._percent_str)s|%(progress.downloaded_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s|%(progress.status)s";

    public static ProcessSpec BuildDiscoverySpec(string ytDlpPath, string url) => new(
        ytDlpPath,
        ["--ignore-config", "--dump-single-json", "--skip-download", "--no-playlist", "--", url]);

    public static ProcessSpec BuildDownloadSpec(
        string ytDlpPath,
        string ffmpegPath,
        string formatSelector,
        string outputTemplate,
        string destinationDirectory,
        string taskDirectory,
        string url) => new(
        ytDlpPath,
        [
            "--ignore-config",
            "--no-playlist",
            "--newline",
            "--progress",
            "--no-overwrites",
            "--windows-filenames",
            "--ffmpeg-location", ffmpegPath,
            "--format", formatSelector,
            "--output", outputTemplate,
            "--paths", $"home:{destinationDirectory}",
            "--paths", $"temp:{taskDirectory}",
            "--progress-template", ProgressTemplate,
            "--print", "after_move:VIDORA_RESULT:%(filepath)s",
            "--", url
        ]);
}
