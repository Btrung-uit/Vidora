using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.ComponentModel;
using Vidora.Core.Models;
using Vidora.Core.Services;

namespace Vidora.Features.Settings;

public partial class SettingsViewModel(
    IDependencyService dependencyService,
    bool hasPersistenceRecoveryWarning = false,
    ISettingsService? settingsService = null,
    IThemeService? themeService = null,
    Action<string?>? configureFfmpegPath = null) : ObservableObject
{
    private readonly SemaphoreSlim settingsMutationGate = new(1, 1);
    private bool initializing;
    public bool HasPersistenceRecoveryWarning { get; } = hasPersistenceRecoveryWarning;

    [ObservableProperty]
    public partial string Theme { get; set; } = "System";

    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string? FfmpegPath { get; private set; }

    [ObservableProperty]
    public partial bool HasFfmpegSelectionError { get; private set; }

    [ObservableProperty]
    public partial bool HasSettingsSaveError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseFfmpeg))]
    [NotifyCanExecuteChangedFor(nameof(CheckDependenciesCommand), nameof(UpdateYtDlpCommand), nameof(SelectFfmpegCommand))]
    public partial bool IsSelectingFfmpeg { get; private set; }

    public bool CanChooseFfmpeg => !IsChecking && !IsUpdating && !IsSelectingFfmpeg;

    public async Task InitializeSettingsAsync(CancellationToken cancellationToken)
    {
        if (settingsService is null)
        {
            return;
        }

        initializing = true;
        try
        {
            var settings = await settingsService.LoadAsync(cancellationToken);
            FfmpegPath = settings.FfmpegPath;
            if (FfmpegPath is not null && !Path.IsPathFullyQualified(FfmpegPath))
            {
                FfmpegPath = null;
                HasFfmpegSelectionError = true;
            }
            configureFfmpegPath?.Invoke(FfmpegPath);
            Theme = settings.Theme;
            NotificationsEnabled = settings.NotificationsEnabled;
            themeService?.Apply(Theme);
        }
        finally
        {
            initializing = false;
        }
    }

    partial void OnThemeChanged(string value)
    {
        themeService?.Apply(value);
        _ = SaveSettingsAsync();
    }

    partial void OnNotificationsEnabledChanged(bool value) => _ = SaveSettingsAsync();

    private async Task SaveSettingsAsync()
    {
        if (settingsService is not null && !initializing)
        {
            await settingsMutationGate.WaitAsync();
            try
            {
                await settingsService.UpdateAsync(latest => latest with { Theme = Theme, NotificationsEnabled = NotificationsEnabled }, CancellationToken.None);
                HasSettingsSaveError = false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                HasSettingsSaveError = true;
            }
            finally
            {
                settingsMutationGate.Release();
            }
        }
    }

    private bool CanSelectFfmpeg(string? path) => CanChooseFfmpeg;

    [RelayCommand(CanExecute = nameof(CanSelectFfmpeg))]
    private async Task SelectFfmpegAsync(string? path, CancellationToken cancellationToken)
    {
        HasFfmpegSelectionError = true;
        if (configureFfmpegPath is null || string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            return;
        }

        var previousPath = FfmpegPath;
        var previousReport = Report;
        IsSelectingFfmpeg = true;
        var acquired = false;
        try
        {
            await settingsMutationGate.WaitAsync(cancellationToken);
            acquired = true;
            var component = await dependencyService.CheckFfmpegAsync(path, cancellationToken);
            if (component.Status != DependencyStatus.Ready)
            {
                return;
            }

            configureFfmpegPath(path);
            var report = previousReport is null
                ? await dependencyService.CheckAsync(cancellationToken)
                : previousReport with { Ffmpeg = component };

            if (settingsService is not null)
            {
                await settingsService.UpdateAsync(latest => latest with { FfmpegPath = path }, cancellationToken);
            }

            FfmpegPath = path;
            Report = report;
            HasFfmpegSelectionError = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            HasFfmpegSelectionError = true;
        }
        finally
        {
            if (HasFfmpegSelectionError)
            {
                configureFfmpegPath(previousPath);
                Report = previousReport;
            }
            if (acquired) settingsMutationGate.Release();
            IsSelectingFfmpeg = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    [NotifyPropertyChangedFor(nameof(NeedsSetup), nameof(NeedsYtDlpSetup), nameof(NeedsFfmpegSetup))]
    [NotifyCanExecuteChangedFor(nameof(UpdateYtDlpCommand))]
    public partial DependencyReport? Report { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseFfmpeg))]
    [NotifyCanExecuteChangedFor(nameof(SelectFfmpegCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckDependenciesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateYtDlpCommand))]
    public partial bool IsChecking { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    [NotifyCanExecuteChangedFor(nameof(UpdateYtDlpCommand))]
    public partial string? LatestYtDlpVersion { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseFfmpeg))]
    [NotifyCanExecuteChangedFor(nameof(SelectFfmpegCommand))]
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

    private bool CanCheckDependencies() => CanChooseFfmpeg;

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
        !IsSelectingFfmpeg &&
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
