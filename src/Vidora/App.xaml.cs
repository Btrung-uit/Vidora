using System.Windows;
using Vidora.Features.Shell;

namespace Vidora;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        MainWindow = new MainWindow(new MainWindowViewModel());
        MainWindow.Show();
    }
}
