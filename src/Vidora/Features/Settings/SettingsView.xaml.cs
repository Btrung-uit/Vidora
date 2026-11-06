using System.Windows.Controls;
using System.Windows;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Vidora.Features.Settings;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void ChooseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel || !viewModel.CanChooseFfmpeg) return;
        var dialog = new OpenFileDialog
        {
            Title = FindResource("Dependency.ChooseFfmpeg") as string,
            Filter = "FFmpeg (ffmpeg.exe)|ffmpeg.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            await viewModel.SelectFfmpegCommand.ExecuteAsync(dialog.FileName);
        }
    }
}
