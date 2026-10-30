using System.IO;

namespace Vidora.Infrastructure.Dependencies;

public sealed record AppToolPaths(
    string YtDlpPath,
    string FfmpegPath)
{
    public static AppToolPaths CreateDefault() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Vidora",
            "tools",
            "yt-dlp.exe"),
        Path.Combine(
            AppContext.BaseDirectory,
            "tools",
            "ffmpeg",
            "ffmpeg.exe"));
}
