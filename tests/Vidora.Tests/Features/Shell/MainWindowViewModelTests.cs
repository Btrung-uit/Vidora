using System.Windows;
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
    public void NavigateCommand_Settings_ShowsSettingsView()
    {
        if (Application.Current is null)
        {
            var application = new App();
            application.InitializeComponent();
        }

        var shellViewModel = new MainWindowViewModel();
        var settingsViewModel = new SettingsViewModel(new StubDependencyService());
        var window = new MainWindow(
            shellViewModel,
            new DownloadViewModel(new StubDiscoveryService()),
            settingsViewModel);

        try
        {
            window.Show();
            shellViewModel.NavigateCommand.Execute(ShellPage.Settings);
            window.UpdateLayout();

            var settingsView = LogicalTreeHelper.FindLogicalNode(window, "SettingsContent") as SettingsView;
            Assert.IsNotNull(settingsView);
            Assert.AreEqual(Visibility.Visible, settingsView.Visibility);
        }
        finally
        {
            window.Close();
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
}
