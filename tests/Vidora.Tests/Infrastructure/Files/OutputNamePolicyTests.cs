using Vidora.Core.Models;
using Vidora.Infrastructure.Files;

namespace Vidora.Tests.Infrastructure.Files;

[TestClass]
public sealed class OutputNamePolicyTests
{
    [TestMethod]
    public void CreateAvailableBaseName_VietnameseTitle_RemainsReadable()
    {
        using var directory = new TemporaryDirectory();

        var result = OutputNamePolicy.CreateAvailableBaseName(
            directory.Path,
            "Tiêu đề Việt",
            VideoPlatform.YouTube,
            "abc123");

        Assert.AreEqual("Tiêu đề Việt [YouTube-abc123]", result);
    }

    [TestMethod]
    public void CreateAvailableBaseName_InvalidWindowsCharacters_AreRemoved()
    {
        using var directory = new TemporaryDirectory();

        var result = OutputNamePolicy.CreateAvailableBaseName(
            directory.Path,
            "A<>:\"/\\|?*B",
            VideoPlatform.YouTube,
            "abc123");

        Assert.AreEqual("A B [YouTube-abc123]", result);
    }

    [TestMethod]
    public void CreateAvailableBaseName_ReservedTitle_IsMadeSafe()
    {
        using var directory = new TemporaryDirectory();

        var result = OutputNamePolicy.CreateAvailableBaseName(
            directory.Path,
            "CON",
            VideoPlatform.YouTube,
            "abc123");

        Assert.AreEqual("_CON [YouTube-abc123]", result);
    }

    [TestMethod]
    public void CreateAvailableBaseName_LongTitle_IsShortenedAndKeepsIdentitySuffix()
    {
        using var directory = new TemporaryDirectory();

        var result = OutputNamePolicy.CreateAvailableBaseName(
            directory.Path,
            new string('A', 300),
            VideoPlatform.Instagram,
            "media-id");

        Assert.IsLessThanOrEqualTo(120, result.Length);
        StringAssert.EndsWith(result, " [Instagram-media-id]");
    }

    [TestMethod]
    public void CreateAvailableBaseName_LongMediaId_StillHonorsBaseNameLimit()
    {
        using var directory = new TemporaryDirectory();

        var result = OutputNamePolicy.CreateAvailableBaseName(
            directory.Path,
            "Video",
            VideoPlatform.YouTube,
            new string('x', 300));

        Assert.IsLessThanOrEqualTo(120, result.Length);
        StringAssert.StartsWith(result, "Video [YouTube-");
    }

    [TestMethod]
    public void CreateAvailableBaseName_ExistingExtensions_AddsNextCollisionSuffix()
    {
        using var directory = new TemporaryDirectory();
        const string original = "Video [YouTube-abc123]";
        File.WriteAllBytes(Path.Combine(directory.Path, $"{original}.mp4"), []);
        File.WriteAllBytes(Path.Combine(directory.Path, $"{original} (2).webm"), []);

        var result = OutputNamePolicy.CreateAvailableBaseName(
            directory.Path,
            "Video",
            VideoPlatform.YouTube,
            "abc123");

        Assert.AreEqual("Video [YouTube-abc123] (3)", result);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-output-name-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
