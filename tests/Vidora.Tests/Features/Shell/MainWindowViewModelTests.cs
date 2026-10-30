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
}
