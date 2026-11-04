using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace Vidora.Features.Download;

public partial class DownloadView : System.Windows.Controls.UserControl
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

public sealed class ResourceKeyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? System.Windows.Application.Current?.TryFindResource(key) ?? key : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
