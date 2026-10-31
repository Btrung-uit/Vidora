using System.Windows;
using System.Windows.Threading;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Features.Download;
using Vidora.Features.Settings;
using Vidora.Features.Shell;

namespace Vidora.Tests.Features.Shell;

[TestClass]
public sealed class MainWindowViewModelTests
{
    [TestMethod]
    public void Constructor_DefaultsToDownloadPage()
    {
        var viewModel = new MainWindowViewModel();

        Assert.AreEqual(ShellPage.Download, viewModel.CurrentPage);
    }

    [TestMethod]
    public void NavigateCommand_History_SetsHistoryPage()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.NavigateCommand.Execute(ShellPage.History);

        Assert.AreEqual(ShellPage.History, viewModel.CurrentPage);
    }

    [TestMethod]
    public void NavigateCommand_Settings_SetsSettingsPage()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.NavigateCommand.Execute(ShellPage.Settings);

        Assert.AreEqual(ShellPage.Settings, viewModel.CurrentPage);
    }

    [TestMethod]
    public void NavigateCommand_CurrentPage_DoesNotRecreateState()
    {
        var viewModel = new MainWindowViewModel();
        var stateTransitions = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.CurrentPage))
            {
                stateTransitions++;
            }
        };

        viewModel.NavigateCommand.Execute(ShellPage.Download);

        Assert.AreEqual(ShellPage.Download, viewModel.CurrentPage);
        Assert.AreEqual(0, stateTransitions);
    }

    [STATestMethod]
    [DoNotParallelize]
    public void MainWindow_WiresSettingsNavigationAndShutdownOutcomes()
    {
        if (Application.Current is null)
        {
            var application = new App();
            application.InitializeComponent();
        }

        var shellViewModel = new MainWindowViewModel();
        var settingsViewModel = new SettingsViewModel(new StubDependencyService());
        var shutdown = new StubShutdownCoordinator(AppShutdownOutcome.ContinueRunning);
        var window = new MainWindow(
            shellViewModel,
            new DownloadViewModel(
                new StubDiscoveryService(),
                new StubQueueService(),
                new StubSettingsService(),
                TimeProvider.System),
            settingsViewModel,
            shutdown);

        try
        {
            window.Show();
            shellViewModel.NavigateCommand.Execute(ShellPage.Settings);
            window.UpdateLayout();

            var settingsView = LogicalTreeHelper.FindLogicalNode(window, "SettingsContent") as SettingsView;
            Assert.IsNotNull(settingsView);
            Assert.AreEqual(Visibility.Visible, settingsView.Visibility);

            window.Close();

            Assert.IsTrue(window.IsVisible);
            Assert.AreEqual(1, shutdown.Calls);
            shutdown.Outcome = AppShutdownOutcome.ExitCompleted;

            window.Close();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.IsFalse(window.IsVisible);
            Assert.AreEqual(2, shutdown.Calls);
        }
        finally
        {
            if (window.IsVisible)
            {
                shutdown.Outcome = AppShutdownOutcome.ExitCompleted;
                window.Close();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
        }
    }

    private sealed class StubDependencyService : IDependencyService
    {
        public Task<DependencyReport> CheckAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubDiscoveryService : IVideoDiscoveryService
    {
        public Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubQueueService : IDownloadQueueService
    {
        public IReadOnlyList<DownloadQueueItem> Items => [];

        public event EventHandler? Changed { add { } remove { } }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CancelActiveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> RemovePendingAsync(Guid taskId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task PersistInterruptedAsync(Guid taskId, DownloadResult result, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AppSettings(null));

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubShutdownCoordinator(AppShutdownOutcome outcome) : IAppShutdownCoordinator
    {
        public AppShutdownOutcome Outcome { get; set; } = outcome;

        public int Calls { get; private set; }

        public Task<AppShutdownOutcome> RequestShutdownAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Outcome);
        }
    }
}
