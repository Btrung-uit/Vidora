using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Vidora.Infrastructure.Notifications;

namespace Vidora.Tests.Infrastructure.Notifications;

[TestClass]
public sealed class WpfThemeServiceTests
{
    [STATestMethod]
    public void SystemUsesAppPreferenceAndExplicitThemesOverrideIt()
    {
        var resources = new ResourceDictionary();
        var dark = true;
        using var service = new WpfThemeService(resources, Dispatcher.CurrentDispatcher, () => dark, () => false);
        service.Apply("System");
        Assert.AreEqual(Color.FromRgb(17, 17, 17), ((SolidColorBrush)resources["App.BackgroundBrush"]).Color);
        Assert.IsTrue(((SolidColorBrush)resources["App.BackgroundBrush"]).IsFrozen);
        service.Apply("Light");
        Assert.AreEqual(Color.FromRgb(244, 244, 245), ((SolidColorBrush)resources["App.BackgroundBrush"]).Color);
        dark = false;
        service.Apply("Dark");
        Assert.AreEqual(Color.FromRgb(17, 17, 17), ((SolidColorBrush)resources["App.BackgroundBrush"]).Color);
        service.Apply("invalid");
        Assert.AreEqual("System", service.CurrentTheme);
        Assert.AreEqual(Color.FromRgb(244, 244, 245), ((SolidColorBrush)resources["App.BackgroundBrush"]).Color);
    }

    [STATestMethod]
    public void HighContrastOverridesEveryThemeWithPairedColorsAndRestoresSelection()
    {
        var resources = new ResourceDictionary();
        var highContrast = true;
        using var service = new WpfThemeService(resources, Dispatcher.CurrentDispatcher, () => false, () => highContrast);
        foreach (var theme in new[] { "System", "Light", "Dark" })
        {
            service.Apply(theme);
            Assert.AreEqual(SystemColors.WindowBrush, resources["Control.BackgroundBrush"]);
            Assert.AreEqual(SystemColors.WindowTextBrush, resources["Text.PrimaryBrush"]);
            Assert.AreEqual(SystemColors.HighlightBrush, resources["Control.HoverBrush"]);
            Assert.AreEqual(SystemColors.HighlightTextBrush, resources["Control.HoverForegroundBrush"]);
            Assert.AreEqual(SystemColors.HighlightBrush, resources["Control.PressedBrush"]);
            Assert.AreEqual(SystemColors.HighlightTextBrush, resources["Control.PressedForegroundBrush"]);
        }
        highContrast = false;
        service.Apply(service.CurrentTheme);
        Assert.AreEqual("Dark", service.CurrentTheme);
        Assert.AreEqual(Color.FromRgb(17, 17, 17), ((SolidColorBrush)resources["App.BackgroundBrush"]).Color);
    }

    [STATestMethod]
    public void SettingsNotificationQueuesRefreshAndDisposalCancelsPendingRefresh()
    {
        var resources = new ResourceDictionary();
        var dark = false;
        using var service = new WpfThemeService(resources, Dispatcher.CurrentDispatcher, () => dark, () => false);
        service.Apply("System");
        var notification = typeof(WpfThemeService).GetMethod("OnSystemPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        dark = true;
        notification.Invoke(service, [null, new PropertyChangedEventArgs("HighContrast")]);
        PumpDispatcher();
        Assert.AreEqual(Color.FromRgb(17, 17, 17), ((SolidColorBrush)resources["App.BackgroundBrush"]).Color);
        dark = false;
        notification.Invoke(service, [null, new PropertyChangedEventArgs("HighContrast")]);
        service.Dispose();
        PumpDispatcher();
        Assert.AreEqual(Color.FromRgb(17, 17, 17), ((SolidColorBrush)resources["App.BackgroundBrush"]).Color);
        Assert.Throws<ObjectDisposedException>(() => service.Apply("Light"));
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
