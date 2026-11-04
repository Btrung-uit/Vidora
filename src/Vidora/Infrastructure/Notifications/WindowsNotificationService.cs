using System.Windows.Forms;
using Vidora.Core.Services;

namespace Vidora.Infrastructure.Notifications;

public sealed class WindowsNotificationService : INotificationService
{
    private readonly NotifyIcon icon = new()
    {
        Icon = System.Drawing.SystemIcons.Application,
        Visible = true
    };

    public void Show(string title, string message) =>
        icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);

    public void Dispose() => icon.Dispose();
}
