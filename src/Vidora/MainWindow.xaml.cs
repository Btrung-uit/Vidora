using System.Windows;
using Vidora.Features.Settings;
using Vidora.Features.Shell;

namespace Vidora;

public partial class MainWindow : Window
{
    public MainWindow(
        MainWindowViewModel viewModel,
        SettingsViewModel settingsViewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SettingsContent.DataContext = settingsViewModel;
    }
}
