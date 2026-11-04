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
        resources["App.BackgroundBrush"] = Brush(dark ? "#FF111111" : "#FFF4F4F5");
        resources["Navigation.BackgroundBrush"] = Brush(dark ? "#FF181818" : "#FFF9FAFB");
        resources["Content.BackgroundBrush"] = Brush(dark ? "#FF1E1E1E" : "#FFFFFFFF");
        resources["Control.BackgroundBrush"] = Brush(dark ? "#FF242424" : "#FFFFFFFF");
        resources["Control.HoverBrush"] = Brush(dark ? "#FF333333" : "#FFF4F4F5");
        resources["Control.PressedBrush"] = Brush(dark ? "#FF404040" : "#FFE4E4E7");
        resources["Text.PrimaryBrush"] = Brush(dark ? "#FFF7F7F7" : "#FF111111");
        resources["Text.SecondaryBrush"] = Brush(dark ? "#FFD4D4D4" : "#FF595959");
        resources["Border.Brush"] = Brush(dark ? "#FF424242" : "#FFE4E4E7");
        resources["Warning.BackgroundBrush"] = Brush(dark ? "#FF422006" : "#FFFEF3C7");
        resources["Warning.ForegroundBrush"] = Brush(dark ? "#FFFDE68A" : "#FF78350F");
    }

    private static System.Windows.Media.Brush Brush(string color) =>
        new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
