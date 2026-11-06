using System.Windows;
using System.Windows.Controls;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Features.Settings;

namespace Vidora.Tests.Features.Settings;

[TestClass]
public sealed class SettingsViewModelUpdateTests
{
    [TestMethod]
    public async Task CheckDependencies_MissingTools_ExposesConditionalSetupGuidance()
    {
        var report = new DependencyReport(new(DependencyStatus.Missing, null), new(DependencyStatus.Invalid, null));
        var viewModel = new SettingsViewModel(new FakeDependencyService(report, "2026.10.26", Report(DependencyStatus.Ready, "2026.10.26")));
        Assert.IsFalse(viewModel.NeedsSetup);

        await viewModel.CheckDependenciesCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.NeedsSetup);
        Assert.IsTrue(viewModel.NeedsYtDlpSetup);
        Assert.IsTrue(viewModel.NeedsFfmpegSetup);
        await viewModel.UpdateYtDlpCommand.ExecuteAsync(null);
        Assert.IsFalse(viewModel.NeedsSetup);
    }

    [STATestMethod]
    [DoNotParallelize]
    public void SettingsView_SmallHeight_AllSetupActionsRemainScrollable()
    {
        var view = new SettingsView();
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Vidora;component/Resources/Strings.vi-VN.xaml", UriKind.Relative)
        });
        view.Measure(new Size(480, 360));
        view.Arrange(new Rect(0, 0, 480, 360));
        view.UpdateLayout();

        var scroll = view.FindName("SettingsScrollViewer") as ScrollViewer;
        Assert.IsNotNull(scroll);
        Assert.IsGreaterThan(0, scroll.ScrollableHeight);
        scroll.ScrollToEnd();
        view.UpdateLayout();
        var check = (Button)view.FindName("CheckDependenciesButton");
        Assert.IsTrue(check.TransformToAncestor(view).Transform(new Point()).Y < 360);
    }

    [TestMethod]
    public async Task CheckDependenciesCommand_ShowsInstalledAndLatestVersionWithoutUpdating()
    {
        var service = new FakeDependencyService(
            Report(DependencyStatus.Ready, "2026.09.01"),
            "2026.10.26",
            Report(DependencyStatus.Ready, "2026.10.26"));
        var viewModel = new SettingsViewModel(service);

        await viewModel.CheckDependenciesCommand.ExecuteAsync(null);

        Assert.AreEqual("2026.09.01", viewModel.Report?.YtDlp.Version);
        Assert.AreEqual("2026.10.26", viewModel.LatestYtDlpVersion);
        Assert.IsTrue(viewModel.IsUpdateAvailable);
        Assert.IsTrue(viewModel.UpdateYtDlpCommand.CanExecute(null));
        Assert.AreEqual(0, service.UpdateCalls);
    }

    [TestMethod]
    public async Task UpdateYtDlpCommand_ExplicitExecution_InstallsAndRefreshesReport()
    {
        var updated = Report(DependencyStatus.Ready, "2026.10.26");
        var service = new FakeDependencyService(
            Report(DependencyStatus.Missing, null),
            "2026.10.26",
            updated);
        var viewModel = new SettingsViewModel(service);
        await viewModel.CheckDependenciesCommand.ExecuteAsync(null);

        await viewModel.UpdateYtDlpCommand.ExecuteAsync(null);

        Assert.AreEqual(1, service.UpdateCalls);
        Assert.AreSame(updated, viewModel.Report);
        Assert.IsFalse(viewModel.IsUpdating);
        Assert.IsFalse(viewModel.IsUpdateAvailable);
        Assert.IsFalse(viewModel.HasUpdateError);
    }

    [TestMethod]
    public async Task UpdateYtDlpCommand_Failure_KeepsCurrentReportAndShowsRecovery()
    {
        var current = Report(DependencyStatus.Ready, "2026.09.01");
        var service = new FakeDependencyService(current, "2026.10.26", current)
        {
            UpdateException = new IOException("Network interrupted.")
        };
        var viewModel = new SettingsViewModel(service);
        await viewModel.CheckDependenciesCommand.ExecuteAsync(null);

        await viewModel.UpdateYtDlpCommand.ExecuteAsync(null);

        Assert.AreSame(current, viewModel.Report);
        Assert.IsTrue(viewModel.HasUpdateError);
        Assert.IsFalse(viewModel.IsUpdating);
    }

    [TestMethod]
    public async Task CheckDependenciesCommand_HttpTimeout_ShowsRecoveryWithoutThrowing()
    {
        var service = new FakeDependencyService(
            Report(DependencyStatus.Ready, "2026.09.01"),
            "2026.10.26",
            Report(DependencyStatus.Ready, "2026.10.26"))
        {
            LatestException = new TaskCanceledException("HTTP request timed out.")
        };
        var viewModel = new SettingsViewModel(service);

        await viewModel.CheckDependenciesCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.HasUpdateError);
        Assert.IsFalse(viewModel.IsChecking);
    }

    [TestMethod]
    public async Task UpdateYtDlpCommand_HttpTimeout_ShowsRecoveryWithoutThrowing()
    {
        var current = Report(DependencyStatus.Ready, "2026.09.01");
        var service = new FakeDependencyService(current, "2026.10.26", current)
        {
            UpdateException = new TaskCanceledException("HTTP request timed out.")
        };
        var viewModel = new SettingsViewModel(service);
        await viewModel.CheckDependenciesCommand.ExecuteAsync(null);

        await viewModel.UpdateYtDlpCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.HasUpdateError);
        Assert.IsFalse(viewModel.IsUpdating);
    }

    [STATestMethod]
    [DoNotParallelize]
    public void SettingsView_UpdateState_BindsExplicitActionAndRecoveryInstructions()
    {
        var service = new FakeDependencyService(
            Report(DependencyStatus.Missing, null),
            "2026.10.26",
            Report(DependencyStatus.Ready, "2026.10.26"));
        var viewModel = new SettingsViewModel(service);
        viewModel.CheckDependenciesCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        var view = new SettingsView { DataContext = viewModel };
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Vidora;component/Resources/Strings.vi-VN.xaml", UriKind.Relative)
        });
        view.Measure(new Size(700, 700));
        view.Arrange(new Rect(0, 0, 700, 700));
        view.UpdateLayout();

        var latest = (TextBlock)view.FindName("LatestYtDlpVersionText");
        var update = (Button)view.FindName("UpdateYtDlpButton");
        var recovery = (TextBlock)view.FindName("UpdateRecoveryText");
        Assert.AreEqual("2026.10.26", latest.Text);
        Assert.AreEqual(viewModel.UpdateYtDlpCommand, update.Command);
        Assert.AreEqual(view.FindResource("Dependency.UpdateYtDlp"), update.Content);
        Assert.AreEqual(view.FindResource("Dependency.UpdateRecovery"), recovery.Text);
    }

    private static DependencyReport Report(DependencyStatus status, string? version) => new(
        new DependencyComponentReport(status, version),
        new DependencyComponentReport(DependencyStatus.Ready, "7.1.1"));

    private sealed class FakeDependencyService(
        DependencyReport checkReport,
        string latestVersion,
        DependencyReport updateReport) : IDependencyService
    {
        public int UpdateCalls { get; private set; }

        public Exception? UpdateException { get; init; }

        public Exception? LatestException { get; init; }

        public Task<DependencyReport> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(checkReport);

        public Task<string> GetLatestYtDlpVersionAsync(CancellationToken cancellationToken) =>
            LatestException is null
                ? Task.FromResult(latestVersion)
                : Task.FromException<string>(LatestException);

        public Task<DependencyReport> UpdateYtDlpAsync(CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return UpdateException is null
                ? Task.FromResult(updateReport)
                : Task.FromException<DependencyReport>(UpdateException);
        }
    }
}
