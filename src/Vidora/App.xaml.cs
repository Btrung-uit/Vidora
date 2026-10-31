using System.IO;
using System.Windows;
using Vidora.Core.Services;
using Vidora.Features.Download;
using Vidora.Features.Settings;
using Vidora.Features.Shell;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Files;
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
        var taskWorkspaceService = new TaskWorkspaceService(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Vidora",
                "temp"),
            new WindowsCanonicalPathResolver());
        IDownloadService downloadService = new DownloadService(processRunner, toolPaths, taskWorkspaceService);
        IActiveDownloadCoordinator activeDownloads = new ActiveDownloadCoordinator();
        var downloadViewModel = new DownloadViewModel(discoveryService, downloadService, activeDownloads);
        var settingsViewModel = new SettingsViewModel(dependencyService);
        IAppShutdownCoordinator shutdownCoordinator = new AppShutdownCoordinator(
            activeDownloads,
            () => MessageBox.Show(
                MainWindow,
                FindResource("Shutdown.Message") as string ?? string.Empty,
                FindResource("Shutdown.Title") as string ?? string.Empty,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes
                    ? AppShutdownDecision.CancelDownloadAndExit
                    : AppShutdownDecision.ContinueDownloading,
            (_, _, _) => Task.CompletedTask,
            TimeSpan.FromSeconds(5));

        MainWindow = new MainWindow(
            new MainWindowViewModel(),
            downloadViewModel,
            settingsViewModel,
            shutdownCoordinator);
        MainWindow.Show();
        settingsViewModel.CheckDependenciesCommand.Execute(null);
    }
}
