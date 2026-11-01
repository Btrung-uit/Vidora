using System.Text.Json;
using Vidora.Core.Errors;
using Vidora.Infrastructure.Logging;

namespace Vidora.Tests.Infrastructure.Logging;

[TestClass]
public sealed class FileLoggerTests
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private static readonly DateTimeOffset LoggedAt = new(2026, 11, 1, 12, 34, 56, TimeSpan.Zero);

    [TestMethod]
    [DoNotParallelize]
    public async Task WriteAsync_Entry_WritesStructuredRedactedJsonLineWithoutEnvironment()
    {
        using var directory = new TemporaryDirectory();
        const string environmentSecret = "must-not-appear-from-environment";
        Environment.SetEnvironmentVariable("VIDORA_LOG_TEST_SECRET", environmentSecret);
        try
        {
            var operationId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var logger = CreateLogger(directory.Path);

            await logger.WriteAsync(
                new FileLogEntry(
                    FileLogLevel.Error,
                    operationId,
                    "DownloadService",
                    "download-failed",
                    VidoraErrorCode.NetworkFailure,
                    $"Cookie: session=secret\nURL https://example.test/video?token=top-secret at {profile}\\Videos"),
                CancellationToken.None);

            var lines = await File.ReadAllLinesAsync(Path.Combine(directory.Path, "vidora.log"));
            Assert.HasCount(1, lines);
            var line = lines[0];
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.AreEqual(LoggedAt, root.GetProperty("timestamp").GetDateTimeOffset());
            Assert.AreEqual("Error", root.GetProperty("level").GetString());
            Assert.AreEqual(operationId, root.GetProperty("operationId").GetGuid());
            Assert.AreEqual("DownloadService", root.GetProperty("component").GetString());
            Assert.AreEqual("download-failed", root.GetProperty("event").GetString());
            Assert.AreEqual("NetworkFailure", root.GetProperty("errorCode").GetString());
            var detail = root.GetProperty("detail").GetString()!;
            StringAssert.Contains(detail, "Cookie: [REDACTED]");
            StringAssert.Contains(detail, "token=[REDACTED]");
            StringAssert.Contains(detail, "%USERPROFILE%");
            Assert.IsFalse(line.Contains("top-secret", StringComparison.Ordinal));
            Assert.IsFalse(line.Contains(environmentSecret, StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VIDORA_LOG_TEST_SECRET", null);
        }
    }

    [TestMethod]
    public async Task WriteAsync_CrossingTwoMiB_RetainsFiveCompleteRollingFiles()
    {
        using var directory = new TemporaryDirectory();
        var logger = CreateLogger(directory.Path);
        var detail = new string('x', 1_100_000);

        for (var index = 1; index <= 6; index++)
        {
            await logger.WriteAsync(Entry($"event-{index}", detail), CancellationToken.None);
        }

        var files = Directory.GetFiles(directory.Path, "vidora*.log")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.HasCount(5, files);
        CollectionAssert.AreEquivalent(
            new[] { "vidora.log", "vidora.1.log", "vidora.2.log", "vidora.3.log", "vidora.4.log" },
            files.Select(Path.GetFileName).ToArray());
        var events = new List<string>();
        foreach (var file in files)
        {
            Assert.IsLessThanOrEqualTo(MaxFileBytes, new FileInfo(file).Length);
            var lines = await File.ReadAllLinesAsync(file);
            Assert.HasCount(1, lines);
            var line = lines[0];
            using var document = JsonDocument.Parse(line);
            events.Add(document.RootElement.GetProperty("event").GetString()!);
        }

        CollectionAssert.AreEquivalent(
            new[] { "event-2", "event-3", "event-4", "event-5", "event-6" },
            events);
    }

    [TestMethod]
    public async Task WriteAsync_OversizedDetail_TruncatesCompleteEntryBelowTwoMiB()
    {
        using var directory = new TemporaryDirectory();
        var logger = CreateLogger(directory.Path);

        await logger.WriteAsync(
            Entry("oversized", "useful-prefix-" + new string('x', 2_200_000)),
            CancellationToken.None);

        var path = Path.Combine(directory.Path, "vidora.log");
        Assert.IsLessThanOrEqualTo(MaxFileBytes, new FileInfo(path).Length);
        var lines = await File.ReadAllLinesAsync(path);
        Assert.HasCount(1, lines);
        var line = lines[0];
        using var document = JsonDocument.Parse(line);
        var detail = document.RootElement.GetProperty("detail").GetString()!;
        Assert.IsTrue(detail.StartsWith("useful-prefix-", StringComparison.Ordinal));
        Assert.IsTrue(detail.EndsWith("[TRUNCATED]", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WriteAsync_ConcurrentCalls_WritesOneCompleteLinePerEntry()
    {
        using var directory = new TemporaryDirectory();
        var logger = CreateLogger(directory.Path);
        var detail = new string('x', 100_000);

        await Task.WhenAll(Enumerable.Range(1, 50).Select(index =>
            logger.WriteAsync(Entry($"event-{index}", detail), CancellationToken.None)));

        var files = Directory.GetFiles(directory.Path, "vidora*.log");
        Assert.IsLessThanOrEqualTo(5, files.Length);
        var lines = new List<string>();
        foreach (var file in files)
        {
            Assert.IsLessThanOrEqualTo(MaxFileBytes, new FileInfo(file).Length);
            lines.AddRange(await File.ReadAllLinesAsync(file));
        }

        Assert.HasCount(50, lines);
        var events = lines.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("event").GetString();
        }).ToArray();
        Assert.HasCount(50, events.Distinct());
    }

    [TestMethod]
    public async Task WriteAsync_LogDirectoryIsFile_ContainsExpectedIoFailure()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "not-a-directory");
        await File.WriteAllTextAsync(filePath, "keep", CancellationToken.None);
        var logger = CreateLogger(filePath);

        await logger.WriteAsync(Entry("ignored", "detail"), CancellationToken.None);

        Assert.AreEqual("keep", await File.ReadAllTextAsync(filePath));
    }

    private static FileLogger CreateLogger(string path) => new(path, new FixedTimeProvider(LoggedAt));

    private static FileLogEntry Entry(string eventName, string detail) => new(
        FileLogLevel.Information,
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        "TestComponent",
        eventName,
        null,
        detail);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("vidora-file-logger-test-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
