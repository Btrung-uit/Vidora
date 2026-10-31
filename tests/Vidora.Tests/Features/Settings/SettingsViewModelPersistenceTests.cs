using System.Windows;
using System.Windows.Controls;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Features.Settings;

namespace Vidora.Tests.Features.Settings;

[TestClass]
public sealed class SettingsViewModelPersistenceTests
{
    [STATestMethod]
    [DoNotParallelize]
    public void SettingsView_RecoveryDetected_ShowsLocalizedWarning()
    {
        var view = new SettingsView
        {
            DataContext = new SettingsViewModel(new StubDependencyService(), true)
        };
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Vidora;component/Resources/Strings.vi-VN.xaml", UriKind.Relative)
        });

        view.Measure(new Size(600, 700));
        view.Arrange(new Rect(0, 0, 600, 700));
        view.UpdateLayout();

        var warning = view.FindName("PersistenceRecoveryWarning") as TextBlock;
        Assert.IsNotNull(warning);
        Assert.AreEqual(Visibility.Visible, warning.Visibility);
        Assert.AreEqual(view.FindResource("Settings.PersistenceRecoveryWarning"), warning.Text);
    }

    private sealed class StubDependencyService : IDependencyService
    {
        public Task<DependencyReport> CheckAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
