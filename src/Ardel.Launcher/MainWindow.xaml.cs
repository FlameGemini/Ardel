using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinRT.Interop;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.ViewModels;
using Ardel.Launcher.Views;

namespace Ardel.Launcher;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = Loc.Get(LocKeys.Brand_Name);

        RootGrid.Loaded += (_, _) =>
        {
            App.ApplyCaptionButtonColors();
            // MainWindowInstance is set only after this ctor returns; apply fonts on Loaded.
            App.ApplyUiTypography(Loc.ActiveLanguageTag);
        };
        RootGrid.ActualThemeChanged += (_, _) => App.OnRootActualThemeChanged();

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            WindowGeometry.ApplyPreferredMinimum(presenter);
        }

        var display = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        var (size, pos) = WindowGeometry.ComputeDefault(display);
        appWindow.Resize(size);
        appWindow.Move(pos);
    }

    public void ApplyShellFont(FontFamily font)
    {
        if (AppTitleTextBlock is not null)
            AppTitleTextBlock.FontFamily = font;
    }

    private bool _downloadFlyoutBound;
    private Views.GameLogPanel? _gameLogPanel;

    public void ShowGameLogPanel(string instanceDirectory)
    {
        HideGameLogPanel();
        _gameLogPanel = new Views.GameLogPanel(instanceDirectory);
        _gameLogPanel.RequestClose += (_, _) => HideGameLogPanel();
        Grid.SetRowSpan(_gameLogPanel, 2);
        Canvas.SetZIndex(_gameLogPanel, 200);
        RootGrid.Children.Add(_gameLogPanel);
    }

    public void HideGameLogPanel()
    {
        if (_gameLogPanel is null)
            return;
        try { _gameLogPanel.Stop(); } catch { /* ignore */ }
        RootGrid.Children.Remove(_gameLogPanel);
        _gameLogPanel = null;
    }

    public void InitializeNavigation()
    {
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        ApplyLocalization(navigateToTag: "home");
    }

    private void PrewarmPages(string activeTag = "home")
    {
        StartupClock.Mark("PrewarmPages begin");
        _isInternalNavigating = true;
        try
        {
            EnsureDownloadFlyoutBound();

            var pageType = activeTag.ToLowerInvariant() switch
            {
                "download" => typeof(DownloadPage),
                "instances" => typeof(InstancesPage),
                "account" => typeof(AccountPage),
                "settings" => typeof(SettingsPage),
                _ => typeof(HomePage)
            };

            ContentFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo());
            ContentFrame.BackStack.Clear();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Navigation] Initial navigation failed: {ex.Message}");
        }
        finally
        {
            _isInternalNavigating = false;
        }

        HighlightNavTag(activeTag);
        StartupClock.Mark("PrewarmPages done");
    }

    /// <summary>
    /// Refresh chrome strings from <see cref="Loc"/> and rebuild the current page.
    /// </summary>
    /// <param name="navigateToTag">Nav tag to select after refresh; null keeps the current main nav item.</param>
    public void ApplyLocalization(string? navigateToTag = null)
    {
        Title = Loc.Get(LocKeys.Brand_Name);
        if (AppTitleTextBlock is not null)
            AppTitleTextBlock.Text = Loc.Get(LocKeys.Brand_Name);

        App.ApplyUiTypography(Loc.ActiveLanguageTag);

        foreach (var obj in NavView.MenuItems)
        {
            if (obj is not NavigationViewItem item || item.Tag is not string tag)
                continue;

            item.Content = tag switch
            {
                "home" => Loc.Get(LocKeys.Nav_Play),
                "download" => Loc.Get(LocKeys.Nav_Download),
                "instances" => Loc.Get(LocKeys.Nav_Instances),
                "account" => Loc.Get(LocKeys.Nav_Account),
                "settings" => Loc.Get(LocKeys.Nav_Settings),
                _ => item.Content
            };
        }

        // Do not resolve DownloadViewModel on cold start — binds ModSearch/ModDetail.
        // Relocalize flyout chrome only when already bound.
        if (_downloadFlyoutBound)
            DownloadFlyout.Relocalize();

        var preserveTag = navigateToTag;
        if (string.IsNullOrEmpty(preserveTag) &&
            NavView.SelectedItem is NavigationViewItem { Tag: string selectedTag })
        {
            preserveTag = selectedTag;
        }

        // Drop the existing Frame and replace it with a fresh instance to discard all cached NavigationCacheMode.Required pages.
        // WinUI Frame ignores CacheSize=0 for Required pages, which would leave old language instances stuck in memory.
        var oldFrame = ContentFrame;
        var newFrame = new Frame { CacheSize = 8 };
        if (oldFrame is not null)
        {
            oldFrame.Content = null;
            oldFrame.BackStack.Clear();
            oldFrame.ForwardStack.Clear();
        }
        ContentFrame = newFrame;
        NavView.Content = newFrame;

        // Re-prewarm all pages using the newly activated language, ending on preserveTag.
        PrewarmPages(preserveTag ?? "home");
    }

    /// <summary>Resolve and bind the download progress flyout on first need.</summary>
    public void EnsureDownloadFlyoutBound()
    {
        if (_downloadFlyoutBound)
            return;

        StartupClock.Mark("DownloadViewModel resolve begin");
        var downloads = App.Services.GetRequiredService<DownloadViewModel>();
        DownloadFlyout.Bind(downloads);
        _downloadFlyoutBound = true;
        StartupClock.Mark("DownloadViewModel resolve done");
        StartupClock.Flush();
    }

    private bool _isInternalNavigating;

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isInternalNavigating)
            return;

        using var _ = InteractionWatchdog.Profile("NavView_SelectionChanged", 3.0);
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    public void ClearNavSelection()
    {
        _isInternalNavigating = true;
        try
        {
            NavView.SelectedItem = null;
        }
        finally
        {
            _isInternalNavigating = false;
        }
    }

    private void DownloadFlyout_OpenDownloadRequested(object? sender, EventArgs e) =>
        NavigateToDownload();

    public void NavigateToDownload()
    {
        SelectNavTag("download");
    }

    public void NavigateToInstances()
    {
        SelectNavTag("instances");
    }

    /// <summary>Select a nav item without navigating (e.g. after Frame.GoBack).</summary>
    public void HighlightNavTag(string targetTag)
    {
        if (string.IsNullOrWhiteSpace(targetTag))
            return;

        _isInternalNavigating = true;
        try
        {
            foreach (var obj in NavView.MenuItems)
            {
                if (obj is NavigationViewItem item && item.Tag is string tag && tag == targetTag)
                {
                    NavView.SelectedItem = item;
                    return;
                }
            }
        }
        finally
        {
            _isInternalNavigating = false;
        }
    }

    /// <summary>
    /// Local .mrpack file picker/import was unfinished WIP lost on reset —
    /// open Download → Modpacks for catalog installs instead.
    /// </summary>
    public Task PickAndImportModpackAsync()
    {
        var downloads = App.Services.GetRequiredService<DownloadViewModel>();
        downloads.SelectedSection = DownloadSection.Modpack;
        NavigateToDownload();
        return Task.CompletedTask;
    }

    /// <summary>Open Instances and start the given version (progress lives on that page).</summary>
    public void NavigateToInstancesAndLaunch(string versionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        // Select the Instances nav item without running the default parameter-less NavigateTo.
        _isInternalNavigating = true;
        try
        {
            foreach (var obj in NavView.MenuItems)
            {
                if (obj is NavigationViewItem item && item.Tag is string tag && tag == "instances")
                {
                    NavView.SelectedItem = item;
                    break;
                }
            }
        }
        finally
        {
            _isInternalNavigating = false;
        }

        ContentFrame.Navigate(
            typeof(InstancesPage),
            versionId.Trim(),
            new EntranceNavigationTransitionInfo());
    }

    public void NavigateToSettings()
    {
        SelectNavTag("settings");
    }

    public void NavigateToAccount()
    {
        SelectNavTag("account");
    }

    public void NavigateToHome()
    {
        SelectNavTag("home");
    }

    public void NavigateToInstanceSettings(string versionId)
    {
        ClearNavSelection();
        var id = versionId;
        // Let the settings button / list row finish painting before inflating InstanceSettingsPage.
        DispatcherQueue.TryEnqueue(() =>
        {
            ContentFrame.Navigate(
                typeof(InstanceSettingsPage),
                id,
                new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromRight
                });
        });
    }

    public void NavigateToModpackContents(ModpackContentsNavArgs args)
    {
        ClearNavSelection();
        ContentFrame.Navigate(
            typeof(ModpackContentsPage),
            args,
            new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight
            });
    }

    /// <summary>Open Download and show a catalog project detail without kicking off a search.</summary>
    public void OpenCatalogProjectInDownload(ModProjectItem project, ModpackContentsNavArgs? returnToContents = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        EnsureDownloadFlyoutBound();
        var downloads = App.Services.GetRequiredService<DownloadViewModel>();
        downloads.ReturnToModpackContents = returnToContents;
        downloads.OpenCatalogProject(project, CatalogProjectKind.Mod);
        SelectNavTag("download");
    }

    public void SelectNavTag(string targetTag)
    {
        using var _ = InteractionWatchdog.Profile($"SelectNavTag({targetTag})", 3.0);
        foreach (var obj in NavView.MenuItems)
        {
            if (obj is NavigationViewItem item && item.Tag is string tag && tag == targetTag)
            {
                if (!ReferenceEquals(NavView.SelectedItem, item))
                    NavView.SelectedItem = item;
                else
                    NavigateTo(targetTag);
                return;
            }
        }

        NavigateTo(targetTag);
    }

    private void NavigateTo(string tag)
    {
        if (tag.Equals("download", StringComparison.OrdinalIgnoreCase))
        {
            EnsureDownloadFlyoutBound();
            var downloadVm = App.Services.GetRequiredService<DownloadViewModel>();
            // Keep Mod/catalog section when already on the download page (flyout tap, clear finished, etc.).
            if (ContentFrame.Content?.GetType() != typeof(DownloadPage))
                downloadVm.ApplyDownloadPageEntry();
        }

        var pageType = tag switch
        {
            "home" => typeof(HomePage),
            "download" => typeof(DownloadPage),
            "instances" => typeof(InstancesPage),
            "account" => typeof(AccountPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage)
        };

        // Cached pages: skip rebuild + avoid replaying entrance when already visible.
        if (ContentFrame.Content?.GetType() == pageType)
        {
            return;
        }

        ContentFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo());
        ContentFrame.BackStack.Clear();
    }
}
