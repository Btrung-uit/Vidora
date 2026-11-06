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

        Assert.AreEqual("Tiêu đề Việt", result);
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

        Assert.AreEqual("A B", result);
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

        Assert.AreEqual("_CON", result);
    }

    [TestMethod]
    public void CreateAvailableBaseName_LongTitle_IsShortenedWithoutIdentitySuffix()
    {
        using var directory = new TemporaryDirectory();

        var result = OutputNamePolicy.CreateAvailableBaseName(
            directory.Path,
            new string('A', 300),
            VideoPlatform.Instagram,
            "media-id");

        Assert.IsLessThanOrEqualTo(80, result.Length);
        Assert.IsFalse(result.Contains("media-id", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CreateAvailableBaseName_LongMediaId_StillHonorsBaseNameLimit()
    {
        using var directory = new TemporaryDirectory();

        var result = OutputNamePolicy.CreateAvailableBaseName(
            directory.Path,
            "",
            VideoPlatform.YouTube,
            new string('x', 300));

        Assert.IsLessThanOrEqualTo(80, result.Length);
        StringAssert.StartsWith(result, "YouTube - ");
    }

    [TestMethod]
    public void CreateAvailableBaseName_ExistingExtensions_AddsNextCollisionSuffix()
    {
        using var directory = new TemporaryDirectory();
        const string original = "Video";
        File.WriteAllBytes(Path.Combine(directory.Path, $"{original}.mp4"), []);
        File.WriteAllBytes(Path.Combine(directory.Path, $"{original} (2).webm"), []);

        var result = OutputNamePolicy.CreateAvailableBaseName(
            directory.Path,
            "Video",
            VideoPlatform.YouTube,
            "abc123");

        Assert.AreEqual("Video (3)", result);
    }

    [TestMethod]
    [DataRow(VideoPlatform.TikTok, "Bài viết lộ là do AI viết ở cách dựng bài. Mình đã biến nghiên cứu tr", "Bài viết lộ là do AI viết ở cách dựng bài")]
    [DataRow(VideoPlatform.Facebook, "266K views · 8.5K reactions Đang mải chụp ảnh thì... #ToanThayDat #thptqg Toán thầy Đạt - ch", "Đang mải chụp ảnh thì")]
    [DataRow(VideoPlatform.Instagram, "Video by farkin_around", "Instagram - farkin_around")]
    public void CreateAvailableBaseName_SocialCaption_UsesReadableShortTitle(VideoPlatform platform, string title, string expected)
    {
        using var directory = new TemporaryDirectory();
        Assert.AreEqual(expected, OutputNamePolicy.CreateAvailableBaseName(directory.Path, title, platform, "123"));
    }

    [TestMethod]
    public void CreateAvailableBaseName_LongWordsAndEmoji_PreserveTextElementsAndCollisionLimit()
    {
        using var directory = new TemporaryDirectory();
        var title = string.Concat(Enumerable.Repeat("Video 👩‍💻 tiếng Việt ", 15));
        var first = OutputNamePolicy.CreateAvailableBaseName(directory.Path, title, VideoPlatform.YouTube, "one");
        File.WriteAllText(Path.Combine(directory.Path, first + ".mp4"), "keep");
        var second = OutputNamePolicy.CreateAvailableBaseName(directory.Path, title, VideoPlatform.YouTube, "two");
        Assert.IsLessThanOrEqualTo(80, first.Length);
        Assert.IsLessThanOrEqualTo(80, second.Length);
        Assert.IsFalse(char.IsHighSurrogate(first[^1]));
        StringAssert.EndsWith(second, " (2)");
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(directory.Path, first + ".mp4")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-output-name-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
