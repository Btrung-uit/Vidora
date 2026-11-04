using System.IO;

namespace Vidora.Infrastructure.Dependencies;

public sealed record AppToolPaths(
    string YtDlpPath,
    string FfmpegPath)
{
    public static AppToolPaths CreateDefault(
        string? applicationBaseDirectory = null,
        string? localAppDataDirectory = null)
    {
        var appBase = applicationBaseDirectory ?? AppContext.BaseDirectory;
        var localAppData = localAppDataDirectory ??
                           Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ytDlpPath = Path.Combine(localAppData, "Vidora", "tools", "yt-dlp.exe");
        var bundledYtDlpPath = Path.Combine(appBase, "tools", "yt-dlp.exe");
        if (!File.Exists(ytDlpPath) && File.Exists(bundledYtDlpPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ytDlpPath)!);
            File.Copy(bundledYtDlpPath, ytDlpPath);
        }

        return new AppToolPaths(
            ytDlpPath,
            Path.Combine(appBase, "tools", "ffmpeg", "ffmpeg.exe"));
    }
}
