using System.Windows.Controls;

namespace Vidora.Features.History;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && DataContext is HistoryViewModel viewModel)
            {
                viewModel.RefreshAvailability();
            }
        };
    }
}
