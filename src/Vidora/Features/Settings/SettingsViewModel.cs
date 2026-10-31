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
    public partial DependencyReport? Report { get; private set; }

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    public bool IsReady => Report?.IsReady == true;

    [RelayCommand]
    private async Task CheckDependenciesAsync(CancellationToken cancellationToken)
    {
        IsChecking = true;
        try
        {
            Report = await dependencyService.CheckAsync(cancellationToken);
        }
        finally
        {
            IsChecking = false;
        }
    }
}
