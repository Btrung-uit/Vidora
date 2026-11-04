using System.Windows.Controls;

namespace Vidora.Features.History;

public partial class HistoryView : System.Windows.Controls.UserControl
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
