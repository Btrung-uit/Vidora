using System.Globalization;
using Vidora.Core.Models;

namespace Vidora.Infrastructure.Processes;

public static class YtDlpProgressParser
{
    private const string ProgressPrefix = "VIDORA_PROGRESS:";
    private const string ResultPrefix = "VIDORA_RESULT:";

    public static bool TryParseProgress(string line, out DownloadProgress? progress)
    {
        progress = null;
        if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fields = line[ProgressPrefix.Length..].Split('|');
        if (fields.Length != 6 || string.IsNullOrWhiteSpace(fields[5]))
        {
            return false;
        }

        progress = new DownloadProgress(
            ParseDouble(fields[0].Trim().TrimEnd('%')),
            ParseInt64(fields[1]),
            ParseInt64(fields[2]),
            ParseDouble(fields[3]),
            ParseEta(fields[4]),
            fields[5]);
        return true;
    }

    public static bool TryParseFinalPath(string line, out string? finalPath)
    {
        finalPath = null;
        if (!line.StartsWith(ResultPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var value = line[ResultPrefix.Length..];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        finalPath = value;
        return true;
    }

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) &&
        double.IsFinite(result) &&
        result >= 0
            ? result
            : null;

    private static long? ParseInt64(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result >= 0
            ? result
            : null;

    private static TimeSpan? ParseEta(string value)
    {
        var seconds = ParseDouble(value);
        if (seconds is null || seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            return null;
        }

        try
        {
            return TimeSpan.FromSeconds(seconds.Value);
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
