using System.IO;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using Vidora.Core.Models;

namespace Vidora.Infrastructure.Files;

public static class OutputNamePolicy
{
    private const int MaximumBaseNameLength = 80;
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string CreateAvailableBaseName(
        string destinationDirectory,
        string title,
        VideoPlatform platform,
        string mediaId)
    {
        if (platform is VideoPlatform.Facebook or VideoPlatform.TikTok)
        {
            title = Regex.Replace(title,
                @"^\s*(?:[\d.,]+\s*[KMB]?\s+(?:views?|reactions?|likes?|comments?|shares?)\s*[·|•–—-]?\s*)+",
                string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            title = Regex.Replace(title, @"(?:^|\s)#[\p{L}\p{N}_]+", string.Empty);
            var sentenceEnd = Regex.Match(title, @"[.!?](?:\s|$)");
            if (sentenceEnd.Success) title = title[..sentenceEnd.Index];
        }

        if (platform == VideoPlatform.Instagram && title.StartsWith("Video by ", StringComparison.OrdinalIgnoreCase))
        {
            title = "Instagram - " + title["Video by ".Length..];
        }

        var safeTitle = Sanitize(title, string.Empty);
        if (safeTitle.Length == 0) safeTitle = $"{platform} - {Shorten(Sanitize(mediaId, "unknown"), 48)}";
        if (ReservedNames.Contains(safeTitle.Split('.')[0].TrimEnd())) safeTitle = "_" + safeTitle;

        for (var collision = 1; ; collision++)
        {
            var collisionSuffix = collision == 1 ? string.Empty : $" ({collision})";
            var shortenedTitle = Shorten(safeTitle, MaximumBaseNameLength - collisionSuffix.Length);
            if (ReservedNames.Contains(shortenedTitle.Split('.')[0].TrimEnd())) shortenedTitle = "_" + shortenedTitle;
            var candidate = shortenedTitle + collisionSuffix;

            if (!HasCollision(destinationDirectory, candidate))
            {
                return candidate;
            }
        }
    }

    private static string Shorten(string text, int limit)
    {
        if (text.Length <= limit) return text;
        var end = 0;
        foreach (var index in StringInfo.ParseCombiningCharacters(text))
        {
            if (index > limit) break;
            end = index;
        }
        var prefix = text[..end];
        var wordEnd = prefix.LastIndexOf(' ');
        if (end > 0 && text[end] != ' ' && prefix[^1] != ' ' && wordEnd > 0)
            prefix = prefix[..wordEnd];
        return prefix.Length == 0 ? "Video" : prefix.TrimEnd(' ', '.');
    }

    private static string Sanitize(string value, string fallback)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var character in value.Normalize())
        {
            var isInvalid = character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ||
                            char.IsControl(character);
            var output = isInvalid || char.IsWhiteSpace(character) ? ' ' : character;
            if (output == ' ' && previousWasSpace)
            {
                continue;
            }

            builder.Append(output);
            previousWasSpace = output == ' ';
        }

        var result = builder.ToString().Trim(' ', '.');
        return result.Length == 0 ? fallback : result;
    }

    private static bool HasCollision(string directory, string candidate) =>
        Directory.EnumerateFiles(directory)
            .Select(Path.GetFileNameWithoutExtension)
            .Any(existing => string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase));
}
