using System.Windows;
using Vidora.Features.Shell;

namespace Vidora;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
