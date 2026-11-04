using System.Windows;
using System.Windows.Media;
using Vidora.Core.Services;

namespace Vidora.Infrastructure.Notifications;

public sealed class WpfThemeService : IThemeService
{
    private readonly ResourceDictionary resources;
    public string CurrentTheme { get; private set; } = "System";

    public WpfThemeService(System.Windows.Application application) => resources = application.Resources;

    public void Apply(string theme)
    {
        CurrentTheme = theme is "Light" or "Dark" ? theme : "System";
        var dark = CurrentTheme == "Dark" ||
                   (CurrentTheme == "System" && SystemParameters.HighContrast);
        resources["App.BackgroundBrush"] = Brush(dark ? "#FF202124" : "#FFF3F4F6");
        resources["Navigation.BackgroundBrush"] = Brush(dark ? "#FF292A2D" : "#FFFFFFFF");
        resources["Content.BackgroundBrush"] = Brush(dark ? "#FF292A2D" : "#FFFFFFFF");
        resources["Text.SecondaryBrush"] = Brush(dark ? "#FFDADCE0" : "#FF4B5563");
        resources["Border.Brush"] = Brush(dark ? "#FF5F6368" : "#FFD1D5DB");
    }

    private static System.Windows.Media.Brush Brush(string color) =>
        new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
