using System.IO;
using System.Security.Cryptography;
using Vidora.Core.Services;
using Vidora.Infrastructure.Processes;

namespace Vidora.Infrastructure.Dependencies;

public interface IYtDlpUpdater
{
    Task<YtDlpReleaseInfo> UpdateAsync(CancellationToken cancellationToken);
}

public sealed class YtDlpUpdater(
    IYtDlpReleaseClient releaseClient,
    IProcessRunner processRunner,
    AppToolPaths toolPaths,
    IActiveDownloadCoordinator activeDownloads,
    string updateTempRoot) : IYtDlpUpdater
{
    public async Task<YtDlpReleaseInfo> UpdateAsync(CancellationToken cancellationToken)
    {
        EnsureNoActiveDownload();
        var release = await releaseClient.GetLatestAsync(cancellationToken);
        Directory.CreateDirectory(updateTempRoot);
        var workspace = Directory.CreateDirectory(
            Path.Combine(updateTempRoot, $"yt-dlp-{Guid.NewGuid():N}"));
        var stagedPath = Path.Combine(workspace.FullName, "yt-dlp.exe");
        try
        {
            await DownloadAsync(release.ExecutableUri, stagedPath, cancellationToken);
            await VerifyChecksumAsync(stagedPath, release.ExpectedSha256, cancellationToken);
            await VerifyVersionAsync(stagedPath, release.Version, cancellationToken);
            EnsureNoActiveDownload();
            Replace(stagedPath, workspace.FullName);
            return release;
        }
        finally
        {
            TryDeleteDirectory(workspace.FullName);
        }
    }

    private async Task DownloadAsync(
        Uri executableUri,
        string stagedPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            stagedPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await releaseClient.DownloadAssetAsync(executableUri, stream, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task VerifyChecksumAsync(
        string stagedPath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedSha256);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The expected yt-dlp checksum is invalid.", exception);
        }

        await using var stream = new FileStream(
            stagedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken);
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            throw new InvalidDataException("The downloaded yt-dlp checksum does not match.");
        }
    }

    private async Task VerifyVersionAsync(
        string stagedPath,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessSpec(stagedPath, ["--version"]),
            null,
            cancellationToken);
        var actualVersion = result.StandardOutput
            .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line))
            ?.Trim();
        if (result.ExitCode != 0 || !string.Equals(actualVersion, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The downloaded yt-dlp executable did not report the expected version.");
        }
    }

    private void Replace(string stagedPath, string workspace)
    {
        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(toolPaths.YtDlpPath))!;
        Directory.CreateDirectory(targetDirectory);
        if (!File.Exists(toolPaths.YtDlpPath))
        {
            File.Move(stagedPath, toolPaths.YtDlpPath);
            return;
        }

        var backupPath = Path.Combine(workspace, "yt-dlp.previous.exe");
        File.Replace(stagedPath, toolPaths.YtDlpPath, backupPath);
    }

    private void EnsureNoActiveDownload()
    {
        if (activeDownloads.HasActiveDownload)
        {
            throw new InvalidOperationException("yt-dlp cannot be updated while a download is active.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
