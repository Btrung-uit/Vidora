using SystemColors = System.Windows.SystemColors;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Vidora.Core.Services;

namespace Vidora.Infrastructure.Notifications;

public sealed class WpfThemeService : IThemeService, IDisposable
{
    private readonly ResourceDictionary resources;
    private readonly Dispatcher dispatcher;
    private readonly Func<bool> systemDark;
    private readonly Func<bool> highContrast;
    private bool disposed;
    public string CurrentTheme { get; private set; } = "System";

    public WpfThemeService(System.Windows.Application application)
        : this(application.Resources, application.Dispatcher, ReadSystemDark, () => SystemParameters.HighContrast) { }

    public WpfThemeService(ResourceDictionary resources, Dispatcher dispatcher,
        Func<bool> systemDark, Func<bool> highContrast)
    {
        this.resources = resources;
        this.dispatcher = dispatcher;
        this.systemDark = systemDark;
        this.highContrast = highContrast;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemPropertyChanged;
    }

    public void Apply(string theme)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        dispatcher.VerifyAccess();
        CurrentTheme = theme is "Light" or "Dark" ? theme : "System";
        if (highContrast())
        {
            resources["App.BackgroundBrush"] = SystemColors.WindowBrush;
            resources["Navigation.BackgroundBrush"] = SystemColors.WindowBrush;
            resources["Content.BackgroundBrush"] = SystemColors.WindowBrush;
            resources["Control.BackgroundBrush"] = SystemColors.WindowBrush;
            resources["Control.HoverBrush"] = SystemColors.HighlightBrush;
            resources["Control.PressedBrush"] = SystemColors.HighlightBrush;
            resources["Control.HoverForegroundBrush"] = SystemColors.HighlightTextBrush;
            resources["Control.PressedForegroundBrush"] = SystemColors.HighlightTextBrush;
            resources["Text.PrimaryBrush"] = SystemColors.WindowTextBrush;
            resources["Text.SecondaryBrush"] = SystemColors.WindowTextBrush;
            resources["Border.Brush"] = SystemColors.WindowTextBrush;
            resources["Warning.BackgroundBrush"] = SystemColors.WindowBrush;
            resources["Warning.ForegroundBrush"] = SystemColors.WindowTextBrush;
            return;
        }
        var dark = CurrentTheme == "Dark" || (CurrentTheme == "System" && systemDark());
        resources["App.BackgroundBrush"] = Brush(dark ? "#FF111111" : "#FFF4F4F5");
        resources["Navigation.BackgroundBrush"] = Brush(dark ? "#FF181818" : "#FFF9FAFB");
        resources["Content.BackgroundBrush"] = Brush(dark ? "#FF1E1E1E" : "#FFFFFFFF");
        resources["Control.BackgroundBrush"] = Brush(dark ? "#FF242424" : "#FFFFFFFF");
        resources["Control.HoverBrush"] = Brush(dark ? "#FF333333" : "#FFF4F4F5");
        resources["Control.PressedBrush"] = Brush(dark ? "#FF404040" : "#FFE4E4E7");
        resources["Text.PrimaryBrush"] = Brush(dark ? "#FFF7F7F7" : "#FF111111");
        resources["Control.HoverForegroundBrush"] = resources["Text.PrimaryBrush"];
        resources["Control.PressedForegroundBrush"] = resources["Text.PrimaryBrush"];
        resources["Text.SecondaryBrush"] = Brush(dark ? "#FFD4D4D4" : "#FF595959");
        resources["Border.Brush"] = Brush(dark ? "#FF424242" : "#FFE4E4E7");
        resources["Warning.BackgroundBrush"] = Brush(dark ? "#FF422006" : "#FFFEF3C7");
        resources["Warning.ForegroundBrush"] = Brush(dark ? "#FFFDE68A" : "#FF78350F");
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Refresh();
    private void OnSystemPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (disposed || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        dispatcher.BeginInvoke(() =>
        {
            if (!disposed) Apply(CurrentTheme);
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged;
    }

    private static bool ReadSystemDark()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int value && value == 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
