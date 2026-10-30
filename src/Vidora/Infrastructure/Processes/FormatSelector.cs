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
        var audios = formats
            .Where(format => !HasCodec(format.VideoCodec) && HasCodec(format.AudioCodec))
            .ToArray();
        var candidates = videos
            .SelectMany(video => HasCodec(video.AudioCodec)
                ? [new Candidate(video.FormatId, video, IsPreferredVideo(video) && HasPreferredAudioCodec(video))]
                : audios.Select(audio => new Candidate(
                    $"{video.FormatId}+{audio.FormatId}",
                    video,
                    IsPreferredVideo(video) && IsPreferredAudio(audio))))
            .OrderByDescending(candidate => candidate.IsPreferred)
            .ThenByDescending(candidate => candidate.Video.Height ?? 0)
            .ThenByDescending(candidate => candidate.Video.Width ?? 0)
            .ThenByDescending(candidate => candidate.Video.FileSize ?? 0)
            .FirstOrDefault();

        return candidates?.Selector ?? throw Unavailable();
    }

    private static bool IsPreferredVideo(VideoFormat format) =>
        format.Extension.Equals("mp4", StringComparison.OrdinalIgnoreCase) &&
        (format.VideoCodec?.StartsWith("avc1", StringComparison.OrdinalIgnoreCase) == true ||
         format.VideoCodec?.StartsWith("h264", StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsPreferredAudio(VideoFormat format) =>
        format.Extension.Equals("m4a", StringComparison.OrdinalIgnoreCase) &&
        HasPreferredAudioCodec(format);

    private static bool HasPreferredAudioCodec(VideoFormat format) =>
        format.AudioCodec?.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase) == true ||
        format.AudioCodec?.StartsWith("aac", StringComparison.OrdinalIgnoreCase) == true;

    private static bool HasCodec(string? codec) =>
        !string.IsNullOrWhiteSpace(codec) && !codec.Equals("none", StringComparison.OrdinalIgnoreCase);

    private static VidoraException Unavailable() => new(new VidoraError(
        VidoraErrorCode.Unavailable,
        "Error.QualityUnavailable",
        "Action.ChooseAnotherQuality",
        "No compatible format is available for the selected quality.",
        false));

    private sealed record Candidate(string Selector, VideoFormat Video, bool IsPreferred);
}
