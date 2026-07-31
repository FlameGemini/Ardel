using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

public partial class InstancesViewModel : ObservableObject
{
    private readonly LaunchViewModel _launch;
    private readonly SettingsService _settingsService;
    private XamlRoot? _xamlRoot;

    public InstancesViewModel(
        LaunchViewModel launch,
        SettingsService settingsService)
    {
        _launch = launch;
        _settingsService = settingsService;
    }

    public ObservableCollection<GameVersionItem> Instances { get; } = [];

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isBusy;

    public void AttachXamlRoot(XamlRoot? root)
    {
        if (root is not null)
            _xamlRoot = root;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            await _launch.LoadLocalVersionsAsync().ConfigureAwait(true);
            Instances.Clear();
            foreach (var item in _launch.Versions)
                Instances.Add(item);

            IsEmpty = Instances.Count == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            await ShowMessageAsync(
                    Loc.Get(LocKeys.Instances_Title),
                    Loc.Format(LocKeys.Instances_LoadFailed, ex.Message))
                .ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task LaunchAsync(GameVersionItem? item)
    {
        if (item is null || IsBusy || _launch.IsLaunching)
            return;

        IsBusy = true;
        try
        {
            await _launch.LaunchVersionAsync(item).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            await ShowMessageAsync(
                    Loc.Get(LocKeys.Instances_Title),
                    Loc.Format(LocKeys.Home_LaunchFailed, ex.Message))
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenSettings(GameVersionItem? item)
    {
        if (item is null)
            return;

        try
        {
            var settings = _settingsService.Load();
            var dir = GamePaths.GetVersionInstanceDirectory(item.Id, settings.GameDirectory);
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                    Loc.Get(LocKeys.Instances_Title),
                    Loc.Format(LocKeys.Download_CannotOpenFolder, ex.Message))
                .ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(GameVersionItem? item)
    {
        if (item is null || IsBusy)
            return;

        var root = _xamlRoot;
        if (root is null)
        {
            await ShowMessageAsync(
                    Loc.Get(LocKeys.Instances_Title),
                    Loc.Get(LocKeys.Instances_DeleteNoUi))
                .ConfigureAwait(true);
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = root,
            Title = Loc.Get(LocKeys.Instances_DeleteTitle),
            Content = Loc.Format(LocKeys.Instances_DeleteConfirm, item.Id),
            PrimaryButtonText = Loc.Get(LocKeys.Action_Delete),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

        IsBusy = true;
        try
        {
            var settings = _settingsService.Load();
            await GamePaths
                .DeleteInstalledVersionAsync(item.Id, settings.GameDirectory)
                .ConfigureAwait(true);

            if (_launch.SelectedVersion is not null &&
                string.Equals(_launch.SelectedVersion.Id, item.Id, StringComparison.OrdinalIgnoreCase))
            {
                _launch.SelectedVersion = null;
            }

            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            await ShowMessageAsync(
                    Loc.Get(LocKeys.Instances_DeleteTitle),
                    Loc.Format(LocKeys.Instances_DeleteFailed, item.Id, ex.Message))
                .ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var root = _xamlRoot;
        if (root is null)
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel)
        };
        await dialog.ShowAsync();
    }
}
