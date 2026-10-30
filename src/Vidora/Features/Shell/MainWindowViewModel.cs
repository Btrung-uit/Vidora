using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vidora.Features.Shell;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ShellPage CurrentPage { get; set; } = ShellPage.Download;

    [RelayCommand]
    private void Navigate(ShellPage page) => CurrentPage = page;
}
