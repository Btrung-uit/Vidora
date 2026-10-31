using System.IO;
using System.Text;
using Vidora.Core.Models;

namespace Vidora.Infrastructure.Files;

public static class OutputNamePolicy
{
    private const int MaximumBaseNameLength = 120;
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
        var safeTitle = Sanitize(title, "Video");
        if (ReservedNames.Contains(safeTitle.Split('.')[0]))
        {
            safeTitle = $"_{safeTitle}";
        }

        var safeId = Sanitize(mediaId, "unknown");
        var maximumIdLength = Math.Min(
            48,
            Math.Max(1, MaximumBaseNameLength - 1 - $" [{platform}-]".Length));
        if (safeId.Length > maximumIdLength)
        {
            safeId = safeId[..maximumIdLength];
        }

        var identity = $" [{platform}-{safeId}]";
        for (var collision = 1; ; collision++)
        {
            var collisionSuffix = collision == 1 ? string.Empty : $" ({collision})";
            var titleLimit = Math.Max(1, MaximumBaseNameLength - identity.Length - collisionSuffix.Length);
            var shortenedTitle = safeTitle.Length > titleLimit
                ? safeTitle[..titleLimit].TrimEnd(' ', '.')
                : safeTitle;
            var candidate = $"{shortenedTitle}{identity}{collisionSuffix}";

            if (!HasCollision(destinationDirectory, candidate))
            {
                return candidate;
            }
        }
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
