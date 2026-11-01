using System.ComponentModel;
using System.Windows;
using Vidora.Core.Services;
using Vidora.Features.Download;
using Vidora.Features.History;
using Vidora.Features.Settings;
using Vidora.Features.Shell;

namespace Vidora;

public partial class MainWindow : Window
{
    private readonly IAppShutdownCoordinator shutdownCoordinator;
    private bool allowClose;
    private bool closeInProgress;

    public MainWindow(
        MainWindowViewModel viewModel,
        DownloadViewModel downloadViewModel,
        HistoryViewModel historyViewModel,
        SettingsViewModel settingsViewModel,
        IAppShutdownCoordinator shutdownCoordinator)
    {
        InitializeComponent();
        this.shutdownCoordinator = shutdownCoordinator;
        DataContext = viewModel;
        DownloadContent.DataContext = downloadViewModel;
        HistoryContent.DataContext = historyViewModel;
        SettingsContent.DataContext = settingsViewModel;
        Closing += Window_Closing;
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (closeInProgress)
        {
            return;
        }

        closeInProgress = true;
        try
        {
            if (await shutdownCoordinator.RequestShutdownAsync(CancellationToken.None) ==
                AppShutdownOutcome.ExitCompleted)
            {
                allowClose = true;
                await Dispatcher.InvokeAsync(Close);
            }
        }
        finally
        {
            closeInProgress = false;
        }
    }
}
