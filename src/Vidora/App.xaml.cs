using System.Diagnostics;
using System.IO;
using System.Windows;
using Vidora.Core.Services;
using Vidora.Features.Download;
using Vidora.Features.History;
using Vidora.Features.Settings;
using Vidora.Features.Shell;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Files;
using Vidora.Infrastructure.Logging;
using Vidora.Infrastructure.Persistence;
using Vidora.Infrastructure.Processes;

namespace Vidora;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IProcessRunner processRunner = new ProcessRunner();
        var toolPaths = AppToolPaths.CreateDefault();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        IAppLogger logger = new FileLogger(Path.Combine(localAppData, "Vidora", "logs"), TimeProvider.System);
        IDependencyService dependencyService = new DependencyService(
            processRunner,
            toolPaths);
        IVideoDiscoveryService discoveryService = new VideoDiscoveryService(
            processRunner,
            dependencyService,
            toolPaths,
            logger);
        var taskWorkspaceService = new TaskWorkspaceService(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Vidora",
                "temp"),
            new WindowsCanonicalPathResolver());
        IDownloadService downloadService = new DownloadService(
            processRunner,
            toolPaths,
            taskWorkspaceService,
            logger);
        IActiveDownloadCoordinator activeDownloads = new ActiveDownloadCoordinator();
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Vidora",
            "data");
        var queueStore = new QueueStore(Path.Combine(dataDirectory, "queue.json"), TimeProvider.System);
        var historyService = new HistoryService(
            Path.Combine(dataDirectory, "history.json"),
            TimeProvider.System);
        var queueService = new DownloadQueueService(
            downloadService,
            queueStore,
            activeDownloads,
            historyService,
            TimeProvider.System);
        var settingsStore = new SettingsStore(
            Path.Combine(dataDirectory, "settings.json"),
            TimeProvider.System);
        ISettingsService settingsService = settingsStore;
        await queueService.InitializeAsync(CancellationToken.None);
        var downloadViewModel = new DownloadViewModel(
            discoveryService,
            queueService,
            settingsService,
            TimeProvider.System);
        await downloadViewModel.InitializeAsync(CancellationToken.None);
        var historyViewModel = new HistoryViewModel(
            historyService,
            queueService,
            path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }),
            Clipboard.SetText,
            () => MessageBox.Show(
                MainWindow,
                FindResource("History.ClearMessage") as string ?? string.Empty,
                FindResource("History.ClearTitle") as string ?? string.Empty,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes,
            TimeProvider.System);
        await historyViewModel.InitializeAsync(CancellationToken.None);
        var settingsViewModel = new SettingsViewModel(
            dependencyService,
            IsPersistenceRecovery(queueStore.LastLoadStatus) ||
            IsPersistenceRecovery(historyService.LastLoadStatus) ||
            IsPersistenceRecovery(settingsStore.LastLoadStatus));
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
            queueService.PersistInterruptedAsync,
            TimeSpan.FromSeconds(5));

        MainWindow = new MainWindow(
            new MainWindowViewModel(),
            downloadViewModel,
            historyViewModel,
            settingsViewModel,
            shutdownCoordinator);
        MainWindow.Show();
        settingsViewModel.CheckDependenciesCommand.Execute(null);
    }

    private static bool IsPersistenceRecovery(AtomicStoreLoadStatus status) =>
        status is AtomicStoreLoadStatus.RecoveredCorrupt or AtomicStoreLoadStatus.UnsupportedSchema;
}
