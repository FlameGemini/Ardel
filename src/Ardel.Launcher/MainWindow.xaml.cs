using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinRT.Interop;
using Ardel.Launcher.Localization;
using Ardel.Launcher.ViewModels;
using Ardel.Launcher.Views;

namespace Ardel.Launcher;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1200;
    private const int MinWidth = 1280;
    private const int MinHeight = 800;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = Loc.Get(LocKeys.Brand_Name);

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinWidth;
            presenter.PreferredMinimumHeight = MinHeight;
        }

        var display = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        var work = display.WorkArea;
        // Prefer most of the work area so the first launch is not a small floating window.
        var width = Math.Clamp((int)(work.Width * 0.92), MinWidth, Math.Max(MinWidth, work.Width - 32));
        var height = Math.Clamp((int)(work.Height * 0.90), MinHeight, Math.Max(MinHeight, work.Height - 32));
        if (width < DefaultWidth && work.Width >= DefaultWidth + 32)
            width = DefaultWidth;
        if (height < DefaultHeight && work.Height >= DefaultHeight + 32)
            height = DefaultHeight;

        appWindow.Resize(new SizeInt32(width, height));

        var x = work.X + Math.Max(0, (work.Width - width) / 2);
        var y = work.Y + Math.Max(0, (work.Height - height) / 2);
        appWindow.Move(new PointInt32(x, y));
    }

    public void InitializeNavigation()
    {
        SystemBackdrop = null;
        ApplyLocalization(navigateToSettings: false);
    }

    /// <summary>
    /// Refresh chrome strings from <see cref="Loc"/> and rebuild the current page
    /// (pages use Disabled cache so MarkupExtensions re-run).
    /// </summary>
    public void ApplyLocalization(bool navigateToSettings = true)
    {
        Title = Loc.Get(LocKeys.Brand_Name);
        if (AppTitleTextBlock is not null)
            AppTitleTextBlock.Text = Loc.Get(LocKeys.Brand_Name);

        foreach (var obj in NavView.MenuItems)
        {
            if (obj is not NavigationViewItem item || item.Tag is not string tag)
                continue;

            item.Content = tag switch
            {
                "home" => Loc.Get(LocKeys.Nav_Play),
                "download" => Loc.Get(LocKeys.Nav_Download),
                "instances" => Loc.Get(LocKeys.Nav_Instances),
                "settings" => Loc.Get(LocKeys.Nav_Settings),
                _ => item.Content
            };
        }

        var downloads = App.Services.GetRequiredService<DownloadViewModel>();
        DownloadFlyout.Bind(downloads);

        // Drop any cached page instance so {loc:String} resolves again.
        ContentFrame.Content = null;
        ContentFrame.BackStack.Clear();
        ContentFrame.ForwardStack.Clear();

        if (navigateToSettings)
        {
            NavigateToSettings();
            return;
        }

        if (NavView.MenuItems.Count > 0 && NavView.MenuItems[0] is NavigationViewItem home)
        {
            NavView.SelectedItem = home;
            NavigateTo("home");
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            NavigateTo(tag);
    }

    private void DownloadFlyout_OpenDownloadRequested(object? sender, EventArgs e) =>
        NavigateToDownload();

    public void NavigateToDownload()
    {
        foreach (var obj in NavView.MenuItems)
        {
            if (obj is NavigationViewItem item && item.Tag is string tag && tag == "download")
            {
                if (!ReferenceEquals(NavView.SelectedItem, item))
                    NavView.SelectedItem = item;
                else
                    NavigateTo("download");
                return;
            }
        }

        NavigateTo("download");
    }

    public void NavigateToSettings()
    {
        foreach (var obj in NavView.MenuItems)
        {
            if (obj is NavigationViewItem item && item.Tag is string tag && tag == "settings")
            {
                if (!ReferenceEquals(NavView.SelectedItem, item))
                    NavView.SelectedItem = item;
                else
                    NavigateTo("settings");
                return;
            }
        }

        NavigateTo("settings");
    }

    private void NavigateTo(string tag)
    {
        var pageType = tag switch
        {
            "home" => typeof(HomePage),
            "download" => typeof(DownloadPage),
            "instances" => typeof(InstancesPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage)
        };

        ContentFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo());
    }
}
