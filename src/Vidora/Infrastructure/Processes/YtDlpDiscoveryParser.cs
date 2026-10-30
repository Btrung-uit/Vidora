using System.Text.Json;
using Vidora.Core.Errors;
using Vidora.Core.Models;

namespace Vidora.Infrastructure.Processes;

public static class YtDlpDiscoveryParser
{
    public static VideoMetadata Parse(string json, string sourceUrl)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return new VideoMetadata(
                new Uri(sourceUrl, UriKind.Absolute),
                ParsePlatform(RequiredString(root, "extractor_key")),
                RequiredString(root, "id"),
                RequiredString(root, "title"),
                OptionalString(root, "uploader"),
                OptionalDouble(root, "duration") is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                OptionalUri(root, "thumbnail"),
                ParseFormats(root));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw DiscoveryFailure(exception);
        }
    }

    private static IReadOnlyList<VideoFormat> ParseFormats(JsonElement root)
    {
        if (!root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<VideoFormat>();
        foreach (var format in formats.EnumerateArray())
        {
            var formatId = OptionalString(format, "format_id");
            var extension = OptionalString(format, "ext");
            if (formatId is null || extension is null)
            {
                continue;
            }

            result.Add(new VideoFormat(
                formatId,
                extension,
                OptionalInt32(format, "width"),
                OptionalInt32(format, "height"),
                OptionalString(format, "vcodec"),
                OptionalString(format, "acodec"),
                OptionalInt64(format, "filesize") ?? OptionalInt64(format, "filesize_approx")));
        }

        return result;
    }

    private static VideoPlatform ParsePlatform(string extractor) => extractor.ToLowerInvariant() switch
    {
        var value when value.Contains("youtube") => VideoPlatform.YouTube,
        var value when value.Contains("facebook") => VideoPlatform.Facebook,
        var value when value.Contains("instagram") => VideoPlatform.Instagram,
        var value when value.Contains("tiktok") => VideoPlatform.TikTok,
        _ => VideoPlatform.Unknown
    };

    private static string RequiredString(JsonElement element, string propertyName) =>
        OptionalString(element, propertyName) ??
        throw new InvalidOperationException($"Required metadata field is missing: {propertyName}");

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? OptionalDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var result)
            ? result
            : null;

    private static int? OptionalInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result)
            ? result
            : null;

    private static long? OptionalInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var result)
            ? result
            : null;

    private static Uri? OptionalUri(JsonElement element, string propertyName) =>
        OptionalString(element, propertyName) is { } value &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : null;

    private static VidoraException DiscoveryFailure(Exception exception) => new(new VidoraError(
        VidoraErrorCode.Unknown,
        "Error.DiscoveryFailed",
        "Action.TryAgain",
        exception.Message,
        true));
}
