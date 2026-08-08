using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class InstanceSettingsPage : Page
{
    public InstanceSettingsViewModel ViewModel { get; }

    public InstanceSettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<InstanceSettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        ViewModel.InstanceDeleted += OnInstanceDeleted;
        ViewModel.NavigateToInstancesRequested += OnNavigateToInstancesRequested;
        ViewModel.OpenInstanceRequested += OnOpenInstanceRequested;
        Unloaded += (_, _) =>
        {
            Loaded -= OnLoaded;
            ViewModel.InstanceDeleted -= OnInstanceDeleted;
            ViewModel.NavigateToInstancesRequested -= OnNavigateToInstancesRequested;
            ViewModel.OpenInstanceRequested -= OnOpenInstanceRequested;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        ViewModel.AttachXamlRoot(XamlRoot);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (XamlRoot is not null)
            ViewModel.AttachXamlRoot(XamlRoot);
        if (e.Parameter is string versionId && !string.IsNullOrWhiteSpace(versionId))
            ViewModel.Load(versionId);
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        ViewModel.FlushPendingSave();
        base.OnNavigatingFrom(e);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => NavigateBackToInstances();

    private void OnInstanceDeleted(object? sender, EventArgs e) => NavigateBackToInstances();

    private void OnNavigateToInstancesRequested(object? sender, EventArgs e) => NavigateBackToInstances();

    private void OnOpenInstanceRequested(object? sender, string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return;

        Frame.Navigate(typeof(InstanceSettingsPage), versionId, new EntranceNavigationTransitionInfo());
    }

    private void NavigateBackToInstances()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
            return;
        }

        Frame.Navigate(typeof(InstancesPage), null, new EntranceNavigationTransitionInfo());
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingPanelsContainer is null)
            return;

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(SettingPanelsContainer);
        var compositor = visual.Compositor;

        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0.0f, 0.0f);
        animation.InsertKeyFrame(1.0f, 1.0f);
        animation.Duration = System.TimeSpan.FromMilliseconds(200);

        visual.StartAnimation("Opacity", animation);
    }

    private void PresetIcon_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string glyph)
        {
            ViewModel.SelectPresetIconCommand.Execute(glyph);
        }
    }
}
