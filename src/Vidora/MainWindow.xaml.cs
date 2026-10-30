using System.Windows;
using Vidora.Features.Download;
using Vidora.Features.Settings;
using Vidora.Features.Shell;

namespace Vidora;

public partial class MainWindow : Window
{
    public MainWindow(
        MainWindowViewModel viewModel,
        DownloadViewModel downloadViewModel,
        SettingsViewModel settingsViewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        DownloadContent.DataContext = downloadViewModel;
        SettingsContent.DataContext = settingsViewModel;
    }
}
