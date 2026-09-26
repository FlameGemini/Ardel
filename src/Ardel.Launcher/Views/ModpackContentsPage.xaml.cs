using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class ModpackContentsPage : Page
{
    private ModpackContentsNavArgs? _args;

    public ModpackContentsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _args = e.Parameter as ModpackContentsNavArgs;
        TitleText.Text = _args?.PackTitle ?? Loc.Get(LocKeys.Modpack_ContentsTitle);
        SubtitleText.Text = _args is null
            ? string.Empty
            : Loc.Format(LocKeys.Modpack_ContentsSubtitle, _args.VersionLabel, _args.Contents.Count);

        var items = _args?.Contents ?? Array.Empty<ModpackContentItem>();
        ContentsList.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ContentsList.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => NavigateBackToDownload();

    private void NavigateBackToDownload()
    {
        var downloads = App.Services.GetRequiredService<DownloadViewModel>();
        downloads.MarkSectionIntentional();
        downloads.SelectedSection = DownloadSection.Modpack;

        // Prefer frame back stack so we land on the still-open modpack detail.
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
            if (App.MainWindowInstance is MainWindow main)
                main.NavigateToDownload();
            return;
        }

        if (App.MainWindowInstance is MainWindow mainWindow)
        {
            mainWindow.NavigateToDownload();
            return;
        }

        Frame.Navigate(typeof(DownloadPage), null, new EntranceNavigationTransitionInfo());
    }

    private async void OpenSiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ModpackContentItem item })
            return;
        if (string.IsNullOrWhiteSpace(item.WebPageUrl) ||
            !Uri.TryCreate(item.WebPageUrl, UriKind.Absolute, out var uri))
            return;

        await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private void OpenInLauncherButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ModpackContentItem item } || !item.CanOpenInLauncher)
            return;

        if (App.MainWindowInstance is MainWindow mainWindow)
            mainWindow.OpenCatalogProjectInDownload(item.ToProjectItem(), _args);
    }
}
