using Vidora.Core.Errors;
using Vidora.Core.Models;

namespace Vidora.Infrastructure.Processes;

public static class FormatSelector
{
    public static string Select(IReadOnlyList<VideoFormat> formats, QualityPreset preset)
    {
        var maximumHeight = preset switch
        {
            QualityPreset.P1080 => 1080,
            QualityPreset.P720 => 720,
            QualityPreset.P480 => 480,
            _ => int.MaxValue
        };

        var videos = formats
            .Where(format => HasCodec(format.VideoCodec) &&
                             format.Height is { } height &&
                             height <= maximumHeight)
            .ToArray();
        var preferredVideos = videos.Where(IsPreferredVideo).ToArray();
        var video = Best(preferredVideos.Length > 0 ? preferredVideos : videos);
        if (video is null)
        {
            throw Unavailable();
        }

        if (HasCodec(video.AudioCodec))
        {
            return video.FormatId;
        }

        var audios = formats
            .Where(format => !HasCodec(format.VideoCodec) && HasCodec(format.AudioCodec))
            .ToArray();
        var preferredAudios = audios.Where(IsPreferredAudio).ToArray();
        var audio = (preferredAudios.Length > 0 ? preferredAudios : audios)
            .OrderByDescending(format => format.FileSize ?? 0)
            .FirstOrDefault();

        return audio is null
            ? throw Unavailable()
            : $"{video.FormatId}+{audio.FormatId}";
    }

    private static VideoFormat? Best(IEnumerable<VideoFormat> formats) => formats
        .OrderByDescending(format => format.Height ?? 0)
        .ThenByDescending(format => format.Width ?? 0)
        .ThenByDescending(format => format.FileSize ?? 0)
        .FirstOrDefault();

    private static bool IsPreferredVideo(VideoFormat format) =>
        format.Extension.Equals("mp4", StringComparison.OrdinalIgnoreCase) &&
        (format.VideoCodec?.StartsWith("avc1", StringComparison.OrdinalIgnoreCase) == true ||
         format.VideoCodec?.StartsWith("h264", StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsPreferredAudio(VideoFormat format) =>
        format.Extension.Equals("m4a", StringComparison.OrdinalIgnoreCase) &&
        (format.AudioCodec?.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase) == true ||
         format.AudioCodec?.StartsWith("aac", StringComparison.OrdinalIgnoreCase) == true);

    private static bool HasCodec(string? codec) =>
        !string.IsNullOrWhiteSpace(codec) && !codec.Equals("none", StringComparison.OrdinalIgnoreCase);

    private static VidoraException Unavailable() => new(new VidoraError(
        VidoraErrorCode.Unavailable,
        "Error.QualityUnavailable",
        "Action.ChooseAnotherQuality",
        "No compatible format is available for the selected quality.",
        false));
}
