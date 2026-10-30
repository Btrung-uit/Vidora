using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Vidora.Features.Download;

public partial class DownloadView : UserControl
{
    public DownloadView() => InitializeComponent();

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DownloadViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = FindResource("Download.ChooseFolder") as string,
            InitialDirectory = Directory.Exists(viewModel.DestinationDirectory)
                ? viewModel.DestinationDirectory
                : null
        };
        if (dialog.ShowDialog() == true)
        {
            viewModel.DestinationDirectory = dialog.FolderName;
        }
    }
}
