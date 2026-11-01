using System.Security.Cryptography;
using Vidora.Core.Services;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Dependencies;

[TestClass]
public sealed class YtDlpUpdaterTests
{
    private static readonly byte[] OldExecutable = "old-working-executable"u8.ToArray();
    private static readonly byte[] NewExecutable = "new-verified-executable"u8.ToArray();

    [TestMethod]
    public async Task UpdateAsync_ValidChecksumAndVersion_AtomicallyReplacesExistingExecutable()
    {
        using var files = new UpdateFiles(withExistingExecutable: true);
        var release = Release(NewExecutable);
        var updater = CreateUpdater(files, new FakeReleaseClient(release, NewExecutable), ValidRunner(release));

        var installed = await updater.UpdateAsync(CancellationToken.None);

        Assert.AreEqual(release, installed);
        CollectionAssert.AreEqual(NewExecutable, await File.ReadAllBytesAsync(files.Paths.YtDlpPath));
        Assert.IsEmpty(Directory.GetFileSystemEntries(files.UpdateTempRoot));
    }

    [TestMethod]
    public async Task UpdateAsync_MissingExecutable_InstallsVerifiedFirstRunBinary()
    {
        using var files = new UpdateFiles(withExistingExecutable: false);
        var release = Release(NewExecutable);
        var updater = CreateUpdater(files, new FakeReleaseClient(release, NewExecutable), ValidRunner(release));

        await updater.UpdateAsync(CancellationToken.None);

        CollectionAssert.AreEqual(NewExecutable, await File.ReadAllBytesAsync(files.Paths.YtDlpPath));
    }

    [TestMethod]
    public async Task UpdateAsync_ChecksumMismatch_PreservesExistingExecutable()
    {
        using var files = new UpdateFiles(withExistingExecutable: true);
        var release = Release("different-content"u8.ToArray());
        var updater = CreateUpdater(
            files,
            new FakeReleaseClient(release, NewExecutable),
            new FakeProcessRunner((_, _) => throw new AssertFailedException("Invalid checksum must not execute.")));

        await Assert.ThrowsAsync<InvalidDataException>(() => updater.UpdateAsync(CancellationToken.None));

        CollectionAssert.AreEqual(OldExecutable, await File.ReadAllBytesAsync(files.Paths.YtDlpPath));
        Assert.IsEmpty(Directory.GetFileSystemEntries(files.UpdateTempRoot));
    }

    [TestMethod]
    public async Task UpdateAsync_NetworkInterruption_PreservesExistingExecutable()
    {
        using var files = new UpdateFiles(withExistingExecutable: true);
        var release = Release(NewExecutable);
        var client = new FakeReleaseClient(release, NewExecutable)
        {
            DownloadException = new IOException("Connection interrupted.")
        };
        var updater = CreateUpdater(
            files,
            client,
            new FakeProcessRunner((_, _) => throw new AssertFailedException("Incomplete download must not execute.")));

        await Assert.ThrowsAsync<IOException>(() => updater.UpdateAsync(CancellationToken.None));

        CollectionAssert.AreEqual(OldExecutable, await File.ReadAllBytesAsync(files.Paths.YtDlpPath));
        Assert.IsEmpty(Directory.GetFileSystemEntries(files.UpdateTempRoot));
    }

    [TestMethod]
    public async Task UpdateAsync_InvalidVersionCommand_PreservesExistingExecutable()
    {
        using var files = new UpdateFiles(withExistingExecutable: true);
        var release = Release(NewExecutable);
        var updater = CreateUpdater(
            files,
            new FakeReleaseClient(release, NewExecutable),
            new FakeProcessRunner((_, _) => Task.FromResult(new ProcessResult(1, ["broken"], []))));

        await Assert.ThrowsAsync<InvalidDataException>(() => updater.UpdateAsync(CancellationToken.None));

        CollectionAssert.AreEqual(OldExecutable, await File.ReadAllBytesAsync(files.Paths.YtDlpPath));
        Assert.IsEmpty(Directory.GetFileSystemEntries(files.UpdateTempRoot));
    }

    [TestMethod]
    public async Task UpdateAsync_ReplacementFailure_PreservesExistingExecutable()
    {
        using var files = new UpdateFiles(withExistingExecutable: true);
        var release = Release(NewExecutable);
        var updater = CreateUpdater(files, new FakeReleaseClient(release, NewExecutable), ValidRunner(release));
        await using var lockFile = new FileStream(
            files.Paths.YtDlpPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        await Assert.ThrowsAsync<IOException>(() => updater.UpdateAsync(CancellationToken.None));

        lockFile.Position = 0;
        var actual = new byte[OldExecutable.Length];
        _ = await lockFile.ReadAsync(actual);
        CollectionAssert.AreEqual(OldExecutable, actual);
    }

    [TestMethod]
    public async Task UpdateAsync_ReplacementMovesOldFileThenFails_RestoresExistingExecutable()
    {
        using var files = new UpdateFiles(withExistingExecutable: true);
        var release = Release(NewExecutable);
        var updater = CreateUpdater(
            files,
            new FakeReleaseClient(release, NewExecutable),
            ValidRunner(release),
            replaceFile: (_, destination, backup) =>
            {
                File.Move(destination, backup!);
                throw new IOException("Replacement failed after moving the destination.");
            });

        await Assert.ThrowsAsync<IOException>(() => updater.UpdateAsync(CancellationToken.None));

        CollectionAssert.AreEqual(OldExecutable, await File.ReadAllBytesAsync(files.Paths.YtDlpPath));
        Assert.IsEmpty(Directory.GetFileSystemEntries(files.UpdateTempRoot));
    }

    [TestMethod]
    public async Task UpdateAsync_RestoreFails_PreservesBackupForRecovery()
    {
        using var files = new UpdateFiles(withExistingExecutable: true);
        var release = Release(NewExecutable);
        var updater = CreateUpdater(
            files,
            new FakeReleaseClient(release, NewExecutable),
            ValidRunner(release),
            replaceFile: (_, destination, backup) =>
            {
                File.Move(destination, backup!);
                Directory.CreateDirectory(destination);
                throw new IOException("Replacement failed after moving the destination.");
            });

        var exception = await Assert.ThrowsAsync<IOException>(
            () => updater.UpdateAsync(CancellationToken.None));

        StringAssert.Contains(exception.Message, "could not be restored");
        var backup = Directory.GetFiles(files.UpdateTempRoot, "yt-dlp.previous.exe", SearchOption.AllDirectories);
        Assert.HasCount(1, backup);
        CollectionAssert.AreEqual(OldExecutable, await File.ReadAllBytesAsync(backup[0]));
    }

    [TestMethod]
    public async Task UpdateAsync_VersionCommandStalls_TimesOutAndPreservesExistingExecutable()
    {
        using var files = new UpdateFiles(withExistingExecutable: true);
        var release = Release(NewExecutable);
        var updater = CreateUpdater(
            files,
            new FakeReleaseClient(release, NewExecutable),
            new FakeProcessRunner(async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new AssertFailedException("Unreachable.");
            }),
            verificationTimeout: TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAsync<TimeoutException>(() => updater.UpdateAsync(CancellationToken.None));

        CollectionAssert.AreEqual(OldExecutable, await File.ReadAllBytesAsync(files.Paths.YtDlpPath));
        Assert.IsEmpty(Directory.GetFileSystemEntries(files.UpdateTempRoot));
    }

    [TestMethod]
    public async Task UpdateAsync_ActiveDownload_RejectsBeforeReleaseRequest()
    {
        using var files = new UpdateFiles(withExistingExecutable: true);
        var client = new FakeReleaseClient(Release(NewExecutable), NewExecutable)
        {
            GetLatestException = new AssertFailedException("Active download must block the release request.")
        };
        var updater = CreateUpdater(
            files,
            client,
            ValidRunner(Release(NewExecutable)),
            hasActiveDownload: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.UpdateAsync(CancellationToken.None));

        CollectionAssert.AreEqual(OldExecutable, await File.ReadAllBytesAsync(files.Paths.YtDlpPath));
    }

    private static YtDlpUpdater CreateUpdater(
        UpdateFiles files,
        IYtDlpReleaseClient releaseClient,
        IProcessRunner processRunner,
        bool hasActiveDownload = false,
        Action<string, string, string?>? replaceFile = null,
        TimeSpan? verificationTimeout = null) => new(
        releaseClient,
        processRunner,
        files.Paths,
        new FakeActiveDownloadCoordinator(hasActiveDownload),
        files.UpdateTempRoot,
        replaceFile,
        verificationTimeout);

    private static FakeProcessRunner ValidRunner(YtDlpReleaseInfo release) => new((spec, _) =>
    {
        CollectionAssert.AreEqual(new[] { "--version" }, spec.Arguments.ToArray());
        Assert.IsTrue(File.Exists(spec.FileName));
        return Task.FromResult(new ProcessResult(0, [release.Version], []));
    });

    private static YtDlpReleaseInfo Release(byte[] expectedContent) => new(
        "2026.10.26",
        new Uri("https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.26/yt-dlp.exe"),
        new Uri("https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.26/SHA2-256SUMS"),
        Convert.ToHexString(SHA256.HashData(expectedContent)).ToLowerInvariant());

    private sealed class FakeReleaseClient(
        YtDlpReleaseInfo release,
        byte[] executable) : IYtDlpReleaseClient
    {
        public Exception? GetLatestException { get; init; }

        public Exception? DownloadException { get; init; }

        public Task<YtDlpReleaseInfo> GetLatestAsync(CancellationToken cancellationToken) =>
            GetLatestException is null
                ? Task.FromResult(release)
                : Task.FromException<YtDlpReleaseInfo>(GetLatestException);

        public async Task DownloadAssetAsync(
            Uri assetUri,
            Stream destination,
            CancellationToken cancellationToken)
        {
            var length = DownloadException is null ? executable.Length : executable.Length / 2;
            await destination.WriteAsync(executable.AsMemory(0, length), cancellationToken);
            if (DownloadException is not null)
            {
                throw DownloadException;
            }
        }
    }

    private sealed class FakeProcessRunner(
        Func<ProcessSpec, CancellationToken, Task<ProcessResult>> run) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessSpec spec,
            IProgress<ProcessOutputLine>? output,
            CancellationToken cancellationToken) => run(spec, cancellationToken);
    }

    private sealed class FakeActiveDownloadCoordinator(bool hasActiveDownload) : IActiveDownloadCoordinator
    {
        public bool HasActiveDownload => hasActiveDownload;

        public Guid? ActiveTaskId => null;

        public CancellationToken Begin(Guid taskId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Complete(Guid taskId) => throw new NotSupportedException();

        public Task<bool> CancelAndWaitAsync(
            Guid taskId,
            TimeSpan timeout,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UpdateFiles : IDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("vidora-updater-test-");

        public UpdateFiles(bool withExistingExecutable)
        {
            var tools = Directory.CreateDirectory(Path.Combine(root.FullName, "tools"));
            UpdateTempRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "temp", "updates")).FullName;
            Paths = new AppToolPaths(
                Path.Combine(tools.FullName, "yt-dlp.exe"),
                Path.Combine(tools.FullName, "ffmpeg.exe"));
            if (withExistingExecutable)
            {
                File.WriteAllBytes(Paths.YtDlpPath, OldExecutable);
            }
        }

        public AppToolPaths Paths { get; }

        public string UpdateTempRoot { get; }

        public void Dispose() => root.Delete(recursive: true);
    }
}
