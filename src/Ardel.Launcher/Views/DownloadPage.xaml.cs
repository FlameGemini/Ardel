using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
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

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is not ListViewItem { Tag: string tag })
            return;

        ViewModel.SelectedSection = tag switch
        {
            "mod" => DownloadSection.Mod,
            _ => DownloadSection.Minecraft
        };
    }

    private void SyncSectionListSelection()
    {
        var wantMod = ViewModel.SelectedSection == DownloadSection.Mod;
        var index = wantMod ? 1 : 0;
        if (SectionList.SelectedIndex != index)
            SectionList.SelectedIndex = index;
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

    private void ApplySectionInstant(DownloadSection section)
    {
        var showMod = section == DownloadSection.Mod;
        MinecraftPane.Opacity = showMod ? 0 : 1;
        MinecraftPane.Visibility = showMod ? Visibility.Collapsed : Visibility.Visible;
        MinecraftPane.IsHitTestVisible = !showMod;
        MinecraftPaneOffset.X = 0;

        ModPane.Opacity = showMod ? 1 : 0;
        ModPane.Visibility = showMod ? Visibility.Visible : Visibility.Collapsed;
        ModPane.IsHitTestVisible = showMod;
        ModPaneOffset.X = 0;
    }

    private void AnimateSectionSwitch(DownloadSection from, DownloadSection to)
    {
        StopSectionAnimation();

        var showMod = to == DownloadSection.Mod;
        var incoming = showMod ? ModPane : MinecraftPane;
        var outgoing = showMod ? MinecraftPane : ModPane;
        var incomingOffset = showMod ? ModPaneOffset : MinecraftPaneOffset;
        var outgoingOffset = showMod ? MinecraftPaneOffset : ModPaneOffset;
        var slideInFrom = showMod ? 18d : -18d;
        var slideOutTo = showMod ? -12d : 12d;

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

            outgoing.Visibility = Visibility.Collapsed;
            outgoing.Opacity = 0;
            outgoingOffset.X = 0;
            incoming.Opacity = 1;
            incomingOffset.X = 0;
            incoming.IsHitTestVisible = true;
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
