using Vidora.Core.Models;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class FormatSelectorTests
{
    private static readonly VideoFormat[] Formats =
    [
        new("mp4-1080", "mp4", 1920, 1080, "avc1.640028", "none", 10_000_000),
        new("m4a", "m4a", null, null, "none", "mp4a.40.2", 2_000_000),
        new("webm-1440", "webm", 2560, 1440, "vp9", "none", 15_000_000),
        new("opus", "webm", null, null, "none", "opus", 2_500_000),
        new("muxed-720", "mp4", 1280, 720, "avc1.4d401f", "mp4a.40.2", 8_000_000),
        new("muxed-480", "mp4", 854, 480, "avc1.4d401e", "mp4a.40.2", 5_000_000)
    ];

    [TestMethod]
    [DataRow(QualityPreset.Best, "mp4-1080+m4a")]
    [DataRow(QualityPreset.P1080, "mp4-1080+m4a")]
    [DataRow(QualityPreset.P720, "muxed-720")]
    [DataRow(QualityPreset.P480, "muxed-480")]
    public void Select_QualityPreset_UsesPreferredFormatsWithinHeightLimit(
        QualityPreset preset,
        string expected)
    {
        var selector = FormatSelector.Select(Formats, preset);

        Assert.AreEqual(expected, selector);
        Assert.IsFalse(selector.Contains("recode", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(selector.Contains("convert", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Select_NoPreferredMp4Pair_FallsBackToTruthfulWebmIds()
    {
        VideoFormat[] formats =
        [
            new("webm-video", "webm", 1920, 1080, "vp9", "none", 10_000_000),
            new("webm-audio", "webm", null, null, "none", "opus", 2_000_000)
        ];

        var selector = FormatSelector.Select(formats, QualityPreset.P1080);

        Assert.AreEqual("webm-video+webm-audio", selector);
    }

    [TestMethod]
    public void Select_OnlyFormatsAboveLimit_ThrowsStableUnavailableError()
    {
        VideoFormat[] formats =
        [
            new("video-1080", "mp4", 1920, 1080, "avc1", "none", 10_000_000),
            new("audio", "m4a", null, null, "none", "mp4a", 2_000_000)
        ];

        var exception = Assert.Throws<Core.Errors.VidoraException>(
            () => FormatSelector.Select(formats, QualityPreset.P480));

        Assert.AreEqual(Core.Errors.VidoraErrorCode.Unavailable, exception.Error.Code);
    }
}
