namespace Vidora.Core.Models;

public sealed record VideoFormat(
    string FormatId,
    string Extension,
    int? Width,
    int? Height,
    string? VideoCodec,
    string? AudioCodec,
    long? FileSize);
