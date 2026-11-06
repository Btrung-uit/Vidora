namespace Vidora.Core.Models;

public sealed record AppSettings(
    string? LastDestinationDirectory,
    string Theme = "System",
    bool NotificationsEnabled = true,
    string? FfmpegPath = null);
