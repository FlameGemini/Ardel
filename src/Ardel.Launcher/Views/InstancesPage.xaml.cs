using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.Models;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class InstancesPage : Page
{
    public InstancesViewModel ViewModel { get; }

    public InstancesPage()
    {
        ViewModel = App.Services.GetRequiredService<InstancesViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.AttachXamlRoot(XamlRoot);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (XamlRoot is not null)
            ViewModel.AttachXamlRoot(XamlRoot);
        ViewModel.RefreshCommand.Execute(null);
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameVersionItem item })
            ViewModel.LaunchCommand.Execute(item);
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameVersionItem item })
        {
            Frame.Navigate(
                typeof(InstanceSettingsPage),
                item.Id,
                new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromRight
                });
        }
    }
}
