using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Models;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class InstancesPage : Page
{
    public InstancesViewModel ViewModel { get; }
    private bool _reordering;

    public InstancesPage()
    {
        ViewModel = App.Services.GetRequiredService<InstancesViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.AttachXamlRoot(XamlRoot);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (XamlRoot is not null)
            ViewModel.AttachXamlRoot(XamlRoot);

        if (e.Parameter is string versionId && !string.IsNullOrWhiteSpace(versionId))
            ViewModel.QueueLaunch(versionId);

        // Paint the page shell before any refresh / disk work.
        await Task.Yield();

        ViewModel.RefreshCommand.Execute(null);
    }

    private void InstancesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("InstancesPage.InstancesList_SelectionChanged");
        // Drop selection immediately — reorder needs SelectionMode≠None, but the list should not keep focus.
        if (!_reordering && InstancesList.SelectedItem is not null)
            InstancesList.SelectedItem = null;
    }

    private void InstancesList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _reordering = true;
    }

    private void InstancesList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Defer persist so reposition animation isn't fighting a sync settings write.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            ViewModel.PersistOrder();
            _reordering = false;
            if (InstancesList.SelectedItem is not null)
                InstancesList.SelectedItem = null;
        });
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("InstancesPage.LaunchButton_Click");
        if (sender is FrameworkElement { Tag: GameVersionItem item })
            ViewModel.LaunchCommand.Execute(item);
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("InstancesPage.OpenSettingsButton_Click");
        if (sender is FrameworkElement { Tag: GameVersionItem item })
        {
            if (App.MainWindowInstance is MainWindow mainWindow)
            {
                mainWindow.NavigateToInstanceSettings(item.Id);
            }
            else
            {
                Frame?.Navigate(
                    typeof(InstanceSettingsPage),
                    item.Id,
                    new SlideNavigationTransitionInfo
                    {
                        Effect = SlideNavigationTransitionEffect.FromRight
                    });
            }
        }
    }
}
