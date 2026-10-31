using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class YtDlpProgressParserTests
{
    [TestMethod]
    public void TryParseProgress_CompleteMarker_MapsFields()
    {
        var parsed = YtDlpProgressParser.TryParseProgress(
            "VIDORA_PROGRESS:42.5|1024|2048|512.5|8|downloading",
            out var progress);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(progress);
        Assert.AreEqual(42.5, progress.Percent);
        Assert.AreEqual(1024L, progress.DownloadedBytes);
        Assert.AreEqual(2048L, progress.TotalBytes);
        Assert.AreEqual(512.5, progress.BytesPerSecond);
        Assert.AreEqual(TimeSpan.FromSeconds(8), progress.Eta);
        Assert.AreEqual("downloading", progress.Stage);
    }

    [TestMethod]
    public void TryParseProgress_MissingNumericFields_UsesNulls()
    {
        var parsed = YtDlpProgressParser.TryParseProgress(
            "VIDORA_PROGRESS:NA|1024|NA|NA|NA|postprocessing",
            out var progress);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(progress);
        Assert.IsNull(progress.Percent);
        Assert.AreEqual(1024L, progress.DownloadedBytes);
        Assert.IsNull(progress.TotalBytes);
        Assert.IsNull(progress.BytesPerSecond);
        Assert.IsNull(progress.Eta);
        Assert.AreEqual("postprocessing", progress.Stage);
    }

    [TestMethod]
    public void TryParseProgress_EtaOutsideTimeSpanRange_UsesNull()
    {
        var parsed = YtDlpProgressParser.TryParseProgress(
            "VIDORA_PROGRESS:10|1|10|1|1e308|downloading",
            out var progress);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(progress);
        Assert.IsNull(progress.Eta);
    }

    [TestMethod]
    public void TryParseProgress_NoiseOrMalformedLine_IsIgnored()
    {
        Assert.IsFalse(YtDlpProgressParser.TryParseProgress("[download] 42%", out _));
        Assert.IsFalse(YtDlpProgressParser.TryParseProgress("VIDORA_PROGRESS:42|only-two", out _));
    }

    [TestMethod]
    public void TryParseFinalPath_ResultMarker_PreservesPath()
    {
        const string path = @"D:\Video Việt\Tiêu đề [YouTube-abc].mp4";

        var parsed = YtDlpProgressParser.TryParseFinalPath($"VIDORA_RESULT:{path}", out var finalPath);

        Assert.IsTrue(parsed);
        Assert.AreEqual(path, finalPath);
    }

    [TestMethod]
    public void TryParseFinalPath_NoiseOrEmptyResult_IsIgnored()
    {
        Assert.IsFalse(YtDlpProgressParser.TryParseFinalPath("noise", out _));
        Assert.IsFalse(YtDlpProgressParser.TryParseFinalPath("VIDORA_RESULT:", out _));
    }
}
