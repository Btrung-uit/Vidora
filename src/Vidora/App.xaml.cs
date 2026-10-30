using System.Windows;
using Vidora.Core.Services;
using Vidora.Features.Settings;
using Vidora.Features.Shell;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Processes;

namespace Vidora;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IProcessRunner processRunner = new ProcessRunner();
        IDependencyService dependencyService = new DependencyService(
            processRunner,
            AppToolPaths.CreateDefault());
        var settingsViewModel = new SettingsViewModel(dependencyService);

        MainWindow = new MainWindow(new MainWindowViewModel(), settingsViewModel);
        MainWindow.Show();
        settingsViewModel.CheckDependenciesCommand.Execute(null);
    }
}
