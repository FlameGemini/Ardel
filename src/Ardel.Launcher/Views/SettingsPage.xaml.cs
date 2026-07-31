using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.SyncFromLaunch();
        // Don't auto-scan Java on every visit — Rescan button / first empty list only.
        if (ViewModel.JavaInstallations.Count == 0)
            _ = ViewModel.EnsureJavaScannedCommand.ExecuteAsync(null);
    }
}
