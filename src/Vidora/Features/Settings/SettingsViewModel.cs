using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vidora.Core.Models;
using Vidora.Core.Services;

namespace Vidora.Features.Settings;

public partial class SettingsViewModel(
    IDependencyService dependencyService,
    bool hasPersistenceRecoveryWarning = false) : ObservableObject
{
    public bool HasPersistenceRecoveryWarning { get; } = hasPersistenceRecoveryWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
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
            catch (Exception exception) when (exception is not OperationCanceledException)
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HasUpdateError = true;
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
