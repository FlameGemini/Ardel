using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class DownloadPage : Page
{
    private bool _dialogOpen;
    private bool _listRevealed;

    public DownloadViewModel ViewModel { get; }

    public DownloadPage()
    {
        ViewModel = App.Services.GetRequiredService<DownloadViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadViewModel.IsLoadingVersions)
            or nameof(DownloadViewModel.FilteredVersions))
            UpdateListReveal();
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

    private async void ModSearchButton_Click(object sender, RoutedEventArgs e)
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
