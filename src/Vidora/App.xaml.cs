using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Vidora.Core.Services;
using Vidora.Core.Models;
using Vidora.Features.Download;
using Vidora.Features.History;
using Vidora.Features.Settings;
using Vidora.Features.Shell;
using Vidora.Infrastructure.Dependencies;
using Vidora.Infrastructure.Files;
using Vidora.Infrastructure.Logging;
using Vidora.Infrastructure.Notifications;
using Vidora.Infrastructure.Persistence;
using Vidora.Infrastructure.Processes;

namespace Vidora;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IProcessRunner processRunner = new ProcessRunner();
        var toolPaths = AppToolPaths.CreateDefault();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        IAppLogger logger = new FileLogger(Path.Combine(localAppData, "Vidora", "logs"), TimeProvider.System);
        IActiveDownloadCoordinator activeDownloads = new ActiveDownloadCoordinator();
        var releaseHttpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        IYtDlpReleaseClient releaseClient = new GitHubYtDlpReleaseClient(releaseHttpClient);
        IYtDlpUpdater updater = new YtDlpUpdater(
            releaseClient,
            processRunner,
            toolPaths,
            activeDownloads,
            Path.Combine(localAppData, "Vidora", "temp", "updates"));
        IDependencyService dependencyService = new DependencyService(
            processRunner,
            toolPaths,
            releaseClient,
            updater);
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
        var themeService = new WpfThemeService(this);
        var notificationService = new WindowsNotificationService();
        Exit += (_, _) => notificationService.Dispose();
        await queueService.InitializeAsync(CancellationToken.None);
        var downloadViewModel = new DownloadViewModel(
            discoveryService,
            queueService,
            settingsService,
            TimeProvider.System,
            copyText: System.Windows.Clipboard.SetText);
        await downloadViewModel.InitializeAsync(CancellationToken.None);
        var historyViewModel = new HistoryViewModel(
            historyService,
            queueService,
            path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }),
            System.Windows.Clipboard.SetText,
            () => System.Windows.MessageBox.Show(
                MainWindow,
                FindResource("History.ClearMessage") as string ?? string.Empty,
                FindResource("History.ClearTitle") as string ?? string.Empty,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes,
            TimeProvider.System);
        await historyViewModel.InitializeAsync(CancellationToken.None);
        var notificationStates = new Dictionary<Guid, DownloadStatus>();
        queueService.Changed += async (_, _) =>
        {
            if (MainWindow?.IsActive != false)
            {
                return;
            }

            if (!(await settingsService.LoadAsync(CancellationToken.None)).NotificationsEnabled)
            {
                return;
            }

            foreach (var item in queueService.Items.Where(item =>
                         item.Status is DownloadStatus.Completed or DownloadStatus.Failed))
            {
                if (notificationStates.TryGetValue(item.Request.Id, out var previous) && previous == item.Status)
                {
                    continue;
                }

                notificationStates[item.Request.Id] = item.Status;
                notificationService.Show(
                    item.Status == DownloadStatus.Completed ? "Vidora" : "Vidora",
                    item.Status == DownloadStatus.Completed ? "Tải video hoàn tất." : "Tải video thất bại.");
            }
        };
        var settingsViewModel = new SettingsViewModel(
            dependencyService,
            IsPersistenceRecovery(queueStore.LastLoadStatus) ||
            IsPersistenceRecovery(historyService.LastLoadStatus) ||
            IsPersistenceRecovery(settingsStore.LastLoadStatus),
            settingsService,
            themeService);
        await settingsViewModel.InitializeSettingsAsync(CancellationToken.None);
        IAppShutdownCoordinator shutdownCoordinator = new AppShutdownCoordinator(
            activeDownloads,
            () => System.Windows.MessageBox.Show(
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
