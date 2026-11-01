using Vidora.Infrastructure.Logging;

namespace Vidora.Tests.Infrastructure.Logging;

[TestClass]
public sealed class DiagnosticRedactorTests
{
    private const string Redacted = "[REDACTED]";

    [TestMethod]
    [DataRow("token")]
    [DataRow("ACCESS_TOKEN")]
    [DataRow("auth")]
    [DataRow("Key")]
    [DataRow("signature")]
    [DataRow("access%5Ftoken")]
    [DataRow("%74oken")]
    public void Redact_SensitiveQueryValue_ReplacesOnlyItsValue(string key)
    {
        var input = $"https://example.test/video?id=123&{key}=top-secret&name=kept#preview";

        var actual = DiagnosticRedactor.Redact(input, @"C:\Users\acer");

        Assert.AreEqual(
            $"https://example.test/video?id=123&{key}={Redacted}&name=kept#preview",
            actual);
    }

    [TestMethod]
    public void Redact_QueryKeysContainingSensitiveWords_LeavesThemUnchanged()
    {
        const string input = "https://example.test/video?tokenizer=kept&monkey=also-kept";

        var actual = DiagnosticRedactor.Redact(input, @"C:\Users\acer");

        Assert.AreEqual(input, actual);
    }

    [TestMethod]
    public void Redact_CookieHeaders_RemovesHeaderValues()
    {
        const string input = "Cookie: session=secret; theme=dark\r\n> Set-Cookie: auth=secret\r\nUser-Agent: Vidora";

        var actual = DiagnosticRedactor.Redact(input, @"C:\Users\acer");

        Assert.AreEqual(
            $"Cookie: {Redacted}\r\n> Set-Cookie: {Redacted}\r\nUser-Agent: Vidora",
            actual);
    }

    [TestMethod]
    [DataRow("--cookies C:\\Temp\\cookies.txt --url https://example.test/video", "--cookies [REDACTED] --url https://example.test/video")]
    [DataRow("--cookies=private.txt --verbose", "--cookies=[REDACTED] --verbose")]
    [DataRow("--cookies-from-browser chrome --verbose", "--cookies-from-browser [REDACTED] --verbose")]
    [DataRow("--cookies \"C:\\Temp\\cookie jar.txt\" --verbose", "--cookies [REDACTED] --verbose")]
    public void Redact_SensitiveCookieArgument_RemovesArgumentValue(string input, string expected)
    {
        var actual = DiagnosticRedactor.Redact(input, @"C:\Users\acer");

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Redact_UserProfilePath_ReplacesEveryCaseInsensitiveOccurrence()
    {
        const string input = @"Input C:\Users\Acer\Videos\clip.mp4; temp C:/Users/Acer/AppData/Local/Vidora";

        var actual = DiagnosticRedactor.Redact(input, @"C:\Users\acer");

        Assert.AreEqual(
            @"Input %USERPROFILE%\Videos\clip.mp4; temp %USERPROFILE%/AppData/Local/Vidora",
            actual);
    }

    [TestMethod]
    public void Redact_PathWithProfileNamePrefix_LeavesItUnchanged()
    {
        const string input = @"Another profile C:\Users\acer-other\Videos\clip.mp4";

        var actual = DiagnosticRedactor.Redact(input, @"C:\Users\acer");

        Assert.AreEqual(input, actual);
    }

    [TestMethod]
    public void Redact_NullOrEmptyInput_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, DiagnosticRedactor.Redact(null!, @"C:\Users\acer"));
        Assert.AreEqual(string.Empty, DiagnosticRedactor.Redact(string.Empty, @"C:\Users\acer"));
    }

    [TestMethod]
    public void Redact_OrdinaryTextAndNonProfilePath_RemainsReadable()
    {
        const string input = @"yt-dlp exited with code 1 at D:\Media\video.mp4";

        var actual = DiagnosticRedactor.Redact(input, @"C:\Users\acer");

        Assert.AreEqual(input, actual);
    }
}
