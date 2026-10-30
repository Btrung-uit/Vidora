using Vidora.Core.Errors;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class SupportedUrlValidatorTests
{
    [TestMethod]
    [DataRow("https://www.youtube.com/watch?v=abc123")]
    [DataRow("https://youtu.be/abc123")]
    [DataRow("https://www.facebook.com/watch/?v=123")]
    [DataRow("https://fb.watch/abc123/")]
    [DataRow("https://www.instagram.com/reel/abc123/")]
    [DataRow("https://www.tiktok.com/@creator/video/123")]
    [DataRow("https://vm.tiktok.com/abc123/")]
    public void Validate_SupportedSingleVideoUrl_ReturnsAbsoluteUri(string url)
    {
        var result = SupportedUrlValidator.Validate(url);

        Assert.IsTrue(result.IsAbsoluteUri);
        Assert.AreEqual(url, result.OriginalString);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not a url")]
    [DataRow("ftp://youtube.com/watch?v=abc123")]
    [DataRow("https://youtube.com/playlist?list=PL123")]
    [DataRow("https://youtube.com/@creator")]
    [DataRow("https://facebook.com/creator")]
    [DataRow("https://facebook.com/videos")]
    [DataRow("https://instagram.com/creator")]
    [DataRow("https://tiktok.com/@creator")]
    public void Validate_InvalidOrNonVideoUrl_ThrowsInvalidUrl(string url)
    {
        var exception = Assert.Throws<VidoraException>(() => SupportedUrlValidator.Validate(url));

        Assert.AreEqual(VidoraErrorCode.InvalidUrl, exception.Error.Code);
    }

    [TestMethod]
    [DataRow("https://youtube.com.evil.example/watch?v=abc123")]
    [DataRow("https://notyoutube.com/watch?v=abc123")]
    [DataRow("https://example.com/video/123")]
    public void Validate_UnsupportedOrLookalikeHost_ThrowsUnsupportedSource(string url)
    {
        var exception = Assert.Throws<VidoraException>(() => SupportedUrlValidator.Validate(url));

        Assert.AreEqual(VidoraErrorCode.UnsupportedSource, exception.Error.Code);
    }

    [TestMethod]
    public void BuildDiscoverySpec_UrlWithQuotesAndAmpersand_IsOneArgument()
    {
        const string url = "https://youtube.com/watch?v=abc&title=\"quoted\"&next=calc.exe";

        var spec = YtDlpCommandFactory.BuildDiscoverySpec("C:\\tools\\yt-dlp.exe", url);

        Assert.AreEqual("C:\\tools\\yt-dlp.exe", spec.FileName);
        CollectionAssert.AreEqual(
            new[] { "--ignore-config", "--dump-single-json", "--skip-download", "--no-playlist", "--", url },
            spec.Arguments.ToArray());
        Assert.AreEqual(1, spec.Arguments.Count(argument => argument == url));
    }
}
