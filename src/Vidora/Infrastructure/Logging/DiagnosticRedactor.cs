using System.IO;
using System.Text.RegularExpressions;

namespace Vidora.Infrastructure.Logging;

public static class DiagnosticRedactor
{
    private const string Redacted = "[REDACTED]";
    private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "access_token",
        "auth",
        "key",
        "signature"
    };
    private static readonly Regex CookieHeaderPattern = new(
        @"\b(?<name>Cookie|Set-Cookie)\s*:[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex CookieArgumentPattern = new(
        """(?<prefix>--cookies(?:-from-browser)?(?:=|\s+))(?:(?:"[^"]*")|'[^']*'|\S+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex QueryValuePattern = new(
        @"(?<prefix>[?&])(?<key>[^=&\s?#]+)=(?<value>[^&#\s]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string input, string? userProfilePath = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var redacted = CookieHeaderPattern.Replace(input, "${name}: " + Redacted);
        redacted = CookieArgumentPattern.Replace(redacted, "${prefix}" + Redacted);
        redacted = QueryValuePattern.Replace(redacted, match =>
            SensitiveQueryKeys.Contains(Decode(match.Groups["key"].Value))
                ? match.Groups["prefix"].Value + match.Groups["key"].Value + "=" + Redacted
                : match.Value);

        userProfilePath ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfilePath)
            ? redacted
            : RedactProfilePath(redacted, userProfilePath);
    }

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static string RedactProfilePath(string input, string userProfilePath)
    {
        var profilePath = userProfilePath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            profilePath,
            profilePath.Replace('\\', '/'),
            profilePath.Replace('/', '\\')
        };

        foreach (var variant in variants)
        {
            input = Regex.Replace(
                input,
                Regex.Escape(variant) + @"(?![\p{L}\p{N}_.-])",
                "%USERPROFILE%",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        return input;
    }
}
