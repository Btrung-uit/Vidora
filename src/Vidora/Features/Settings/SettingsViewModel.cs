using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vidora.Core.Models;
using Vidora.Core.Services;

namespace Vidora.Features.Settings;

public partial class SettingsViewModel(
    IDependencyService dependencyService,
    bool hasPersistenceRecoveryWarning = false,
    ISettingsService? settingsService = null,
    IThemeService? themeService = null) : ObservableObject
{
    private AppSettings appSettings = new(null);
    public bool HasPersistenceRecoveryWarning { get; } = hasPersistenceRecoveryWarning;

    [ObservableProperty]
    public partial string Theme { get; set; } = "System";

    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; } = true;

    public async Task InitializeSettingsAsync(CancellationToken cancellationToken)
    {
        if (settingsService is null)
        {
            return;
        }

        var settings = await settingsService.LoadAsync(cancellationToken);
        appSettings = settings;
        Theme = settings.Theme;
        NotificationsEnabled = settings.NotificationsEnabled;
        themeService?.Apply(Theme);
    }

    partial void OnThemeChanged(string value)
    {
        themeService?.Apply(value);
        _ = SaveSettingsAsync();
    }

    partial void OnNotificationsEnabledChanged(bool value) => _ = SaveSettingsAsync();

    private async Task SaveSettingsAsync()
    {
        if (settingsService is not null)
        {
            appSettings = appSettings with { Theme = Theme, NotificationsEnabled = NotificationsEnabled };
            await settingsService.SaveAsync(appSettings, CancellationToken.None);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    [NotifyPropertyChangedFor(nameof(NeedsSetup), nameof(NeedsYtDlpSetup), nameof(NeedsFfmpegSetup))]
    [NotifyCanExecuteChangedFor(nameof(UpdateYtDlpCommand))]
    public partial DependencyReport? Report { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckDependenciesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateYtDlpCommand))]
    public partial bool IsChecking { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    [NotifyCanExecuteChangedFor(nameof(UpdateYtDlpCommand))]
    public partial string? LatestYtDlpVersion { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckDependenciesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateYtDlpCommand))]
    public partial bool IsUpdating { get; private set; }

    [ObservableProperty]
    public partial bool HasUpdateError { get; private set; }

    public bool IsReady => Report?.IsReady == true;

    public bool NeedsSetup => Report is not null && !Report.IsReady;

    public bool NeedsYtDlpSetup => Report is not null && Report.YtDlp.Status != DependencyStatus.Ready;

    public bool NeedsFfmpegSetup => Report is not null && Report.Ffmpeg.Status != DependencyStatus.Ready;

    public bool IsUpdateAvailable =>
        LatestYtDlpVersion is not null &&
        !string.Equals(Report?.YtDlp.Version, LatestYtDlpVersion, StringComparison.Ordinal);

    private bool CanCheckDependencies() => !IsChecking && !IsUpdating;

    [RelayCommand(CanExecute = nameof(CanCheckDependencies))]
    private async Task CheckDependenciesAsync(CancellationToken cancellationToken)
    {
        IsChecking = true;
        try
        {
            Report = await dependencyService.CheckAsync(cancellationToken);
            try
            {
                LatestYtDlpVersion = await dependencyService.GetLatestYtDlpVersionAsync(cancellationToken);
                HasUpdateError = false;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                LatestYtDlpVersion = null;
                HasUpdateError = true;
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    private bool CanUpdateYtDlp() =>
        !IsChecking &&
        !IsUpdating &&
        IsUpdateAvailable;

    [RelayCommand(CanExecute = nameof(CanUpdateYtDlp))]
    private async Task UpdateYtDlpAsync(CancellationToken cancellationToken)
    {
        if (!CanUpdateYtDlp())
        {
            return;
        }

        IsUpdating = true;
        HasUpdateError = false;
        try
        {
            Report = await dependencyService.UpdateYtDlpAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            HasUpdateError = true;
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
