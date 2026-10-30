using System.Windows;
using Vidora.Core.Services;
using Vidora.Features.Download;
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
        var toolPaths = AppToolPaths.CreateDefault();
        IDependencyService dependencyService = new DependencyService(
            processRunner,
            toolPaths);
        IVideoDiscoveryService discoveryService = new VideoDiscoveryService(
            processRunner,
            dependencyService,
            toolPaths);
        IDownloadService downloadService = new DownloadService(processRunner, toolPaths);
        var downloadViewModel = new DownloadViewModel(discoveryService, downloadService);
        var settingsViewModel = new SettingsViewModel(dependencyService);

        MainWindow = new MainWindow(new MainWindowViewModel(), downloadViewModel, settingsViewModel);
        MainWindow.Show();
        settingsViewModel.CheckDependenciesCommand.Execute(null);
    }
}
