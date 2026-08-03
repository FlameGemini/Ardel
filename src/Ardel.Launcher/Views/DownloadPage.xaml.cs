using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class DownloadPage : Page
{
    private static readonly TimeSpan SectionAnimDuration = TimeSpan.FromMilliseconds(220);

    private bool _dialogOpen;
    private bool _listRevealed;
    private DownloadSection? _displayedSection;
    private Storyboard? _sectionStoryboard;
    private int _sectionAnimGeneration;

    public DownloadViewModel ViewModel { get; }

    public DownloadPage()
    {
        ViewModel = App.Services.GetRequiredService<DownloadViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += (_, _) => EnsureSectionVisual(animate: false);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadViewModel.IsLoadingVersions)
            or nameof(DownloadViewModel.FilteredVersions))
            UpdateListReveal();

        if (e.PropertyName is nameof(DownloadViewModel.SelectedSection)
            or nameof(DownloadViewModel.IsMinecraftSection)
            or nameof(DownloadViewModel.IsModSection))
            EnsureSectionVisual(animate: true);
    }

    private void UpdateListReveal()
    {
        if (ViewModel.IsLoadingVersions || ViewModel.FilteredVersions.Count == 0)
        {
            if (!_listRevealed)
                VersionList.Opacity = 0;
            return;
        }

        if (_listRevealed && VersionList.Opacity >= 1)
            return;

        _listRevealed = true;
        var anim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, VersionList);
        Storyboard.SetTargetProperty(anim, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SyncSectionListSelection();
        EnsureSectionVisual(animate: false);
        ViewModel.RefreshGameDirectory();

        // Yield so the page can paint before the first list load / CmlLib touch.
        await Task.Yield();

        if (ViewModel.AllVersions.Count == 0)
            await ViewModel.LoadCommand.ExecuteAsync(null);
        else
            UpdateListReveal();
    }

    private bool _syncingSectionSelection;

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSectionSelection)
            return;

        if (SectionList.SelectedItem is not ListViewItem { Tag: string tag })
            return;

        ViewModel.SelectedSection = tag switch
        {
            "mod" => DownloadSection.Mod,
            "resourcepack" => DownloadSection.ResourcePack,
            "datapack" => DownloadSection.Datapack,
            "shaderpack" => DownloadSection.ShaderPack,
            "modpack" => DownloadSection.Modpack,
            _ => DownloadSection.Minecraft
        };
    }

    private void SyncSectionListSelection()
    {
        _syncingSectionSelection = true;
        try
        {
            var index = ViewModel.SelectedSection switch
            {
                DownloadSection.Minecraft => 0,
                DownloadSection.Mod => 1,
                DownloadSection.ResourcePack => 2,
                DownloadSection.Datapack => 3,
                DownloadSection.ShaderPack => 4,
                DownloadSection.Modpack => 5,
                _ => 0
            };
            if (SectionList.SelectedIndex != index)
                SectionList.SelectedIndex = index;
        }
        finally
        {
            _syncingSectionSelection = false;
        }
    }

    private void EnsureSectionVisual(bool animate)
    {
        var target = ViewModel.SelectedSection;
        if (_displayedSection == target)
            return;

        if (!animate || _displayedSection is null)
        {
            StopSectionAnimation();
            ApplySectionInstant(target);
            _displayedSection = target;
            return;
        }

        AnimateSectionSwitch(_displayedSection.Value, target);
        _displayedSection = target;
    }

    private static bool IsCatalog(DownloadSection section) =>
        section is DownloadSection.Mod or DownloadSection.ResourcePack or DownloadSection.Datapack
            or DownloadSection.ShaderPack or DownloadSection.Modpack;

    private UIElement PaneFor(DownloadSection section) =>
        IsCatalog(section) ? ModPane : MinecraftPane;

    private TranslateTransform OffsetFor(DownloadSection section) =>
        IsCatalog(section) ? ModPaneOffset : MinecraftPaneOffset;

    private void ApplySectionInstant(DownloadSection section)
    {
        ApplyPane(MinecraftPane, MinecraftPaneOffset, section == DownloadSection.Minecraft);
        ApplyPane(ModPane, ModPaneOffset, IsCatalog(section));
    }

    private static void ApplyPane(UIElement pane, TranslateTransform offset, bool show)
    {
        pane.Opacity = show ? 1 : 0;
        pane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        pane.IsHitTestVisible = show;
        offset.X = 0;
    }

    private void AnimateSectionSwitch(DownloadSection from, DownloadSection to)
    {
        StopSectionAnimation();

        if ((IsCatalog(from) && IsCatalog(to)) || from == to)
        {
            ApplySectionInstant(to);
            return;
        }

        var incoming = PaneFor(to);
        var outgoing = PaneFor(from);
        var incomingOffset = OffsetFor(to);
        var outgoingOffset = OffsetFor(from);
        var slideInFrom = 22d;
        var slideOutTo = -14d;

        outgoing.IsHitTestVisible = false;
        incoming.Visibility = Visibility.Visible;
        incoming.Opacity = 0;
        incoming.IsHitTestVisible = false;
        incomingOffset.X = slideInFrom;
        outgoingOffset.X = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(SectionAnimDuration);
        var sb = new Storyboard();
        sb.Children.Add(CreateDoubleAnimation(outgoing, "Opacity", outgoing.Opacity, 0, duration, ease));
        sb.Children.Add(CreateDoubleAnimation(outgoingOffset, "X", outgoingOffset.X, slideOutTo, duration, ease));
        sb.Children.Add(CreateDoubleAnimation(incoming, "Opacity", 0, 1, duration, ease));
        sb.Children.Add(CreateDoubleAnimation(incomingOffset, "X", slideInFrom, 0, duration, ease));

        var generation = ++_sectionAnimGeneration;
        sb.Completed += (_, _) =>
        {
            if (generation != _sectionAnimGeneration)
                return;
            ApplySectionInstant(to);
            _sectionStoryboard = null;
        };

        _sectionStoryboard = sb;
        sb.Begin();
    }

    private void StopSectionAnimation()
    {
        _sectionAnimGeneration++;
        if (_sectionStoryboard is null)
            return;

        try { _sectionStoryboard.Stop(); } catch { /* ignore */ }
        _sectionStoryboard = null;
    }

    private static DoubleAnimation CreateDoubleAnimation(
        DependencyObject target,
        string propertyPath,
        double from,
        double to,
        Duration duration,
        EasingFunctionBase easing)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, propertyPath);
        return anim;
    }

    private async void ModSearchButton_Click(object sender, RoutedEventArgs e) =>
        await RunModSearchAsync();

    private async void ModKeywordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        await RunModSearchAsync();
    }

    private async Task RunModSearchAsync()
    {
        // Editable ComboBox often reverts Text to SelectedItem when focus leaves;
        // commit the control text first so custom versions are not lost.
        ViewModel.ModSearch.CommitVersionInput(ModVersionCombo.Text);
        if (ViewModel.ModSearch.SearchCommand.CanExecute(null))
            await ViewModel.ModSearch.SearchCommand.ExecuteAsync(null);
    }

    private async void ModResultList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ModProjectItem project)
            return;

        ViewModel.ModSearch.CommitVersionInput(ModVersionCombo.Text);
        await ViewModel.ModDetail.OpenAsync(
            project,
            ViewModel.ModSearch.GetActiveHint(),
            ViewModel.ModSearch.ProjectKind);
    }

    private async void ModDependencyList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ModDependencyItem dependency)
            await ViewModel.ModDetail.OpenDependencyAsync(dependency);
    }

    private async void ModFileList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ModFileVersionItem file ||
            ViewModel.ModDetail.Detail is null ||
            XamlRoot is null ||
            _dialogOpen)
            return;

        // Modpack install: pick an instance name, then create loader profile + pack files.
        if (ViewModel.ModDetail.ProjectKind == CatalogProjectKind.Modpack)
        {
            _dialogOpen = true;
            try
            {
                var settings = ViewModel.SnapshotSettingsForInstall();
                await ModpackInstallDialog.ShowAsync(
                    XamlRoot,
                    ViewModel.ModDetail.Detail,
                    file,
                    settings.GameDirectory,
                    ViewModel.StartModpackInstall);
            }
            finally
            {
                _dialogOpen = false;
            }

            return;
        }

        _dialogOpen = true;
        try
        {
            var settings = ViewModel.SnapshotSettingsForInstall();
            var store = App.Services.GetRequiredService<LocalVersionStore>();
            var instances = store.GetInstalled(settings.GameDirectory);

            // Prefer detail-page filters, then fall back to search-page hint.
            var gameHint = ViewModel.ModDetail.SelectedGameVersionFilter is { Id.Length: > 0 } vf
                ? vf.Id
                : ViewModel.ModDetail.HintGameVersion;
            var loaderHint = ViewModel.ModDetail.SelectedLoaderFilter is { Id.Length: > 0 } lf
                ? lf.Id
                : ViewModel.ModDetail.HintLoaderSlug;

            await ModInstallDialog.ShowAsync(
                XamlRoot,
                ViewModel.ModDetail.Detail,
                file,
                instances,
                settings.GameDirectory,
                gameHint,
                loaderHint,
                ViewModel.StartModInstall,
                ViewModel.ModDetail.ProjectKind);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async void VersionList_ItemClick(object sender, ItemClickEventArgs e)
    {
        VersionList.SelectedItem = null;

        if (e.ClickedItem is GameVersionItem version)
            await ShowInstallDialogAsync(version);
    }

    private async Task ShowInstallDialogAsync(GameVersionItem version)
    {
        if (_dialogOpen || XamlRoot is null)
            return;

        _dialogOpen = true;
        try
        {
            var versionsRoot = GamePaths.GetVersionsRoot(ViewModel.GameDirectory);
            var launchService = App.Services.GetRequiredService<Lazy<IMinecraftLaunchService>>().Value;
            var settings = ViewModel.SnapshotSettingsForInstall();
            var request = await InstallVersionDialog.ShowAsync(
                XamlRoot,
                version,
                versionsRoot,
                launchService,
                settings);
            if (request is not null)
                ViewModel.StartInstall(request);
        }
        finally
        {
            _dialogOpen = false;
            VersionList.SelectedItem = null;
        }
    }
}
