using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly LocalVersionStore _localVersions;
    private readonly SettingsService _settingsService;
    private XamlRoot? _xamlRoot;

    public InstancesViewModel(
        LaunchViewModel launch,
        LocalVersionStore localVersions,
        SettingsService settingsService)
    {
        _launch = launch;
        _localVersions = localVersions;
        _settingsService = settingsService;
        _launch.PropertyChanged += OnLaunchPropertyChanged;
    }

    public ObservableCollection<GameVersionItem> Instances { get; } = [];

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isIndeterminate;

    public void AttachXamlRoot(XamlRoot? root)
    {
        if (root is not null)
            _xamlRoot = root;
    }

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            _launch.LoadLocalVersions();
            Instances.Clear();
            foreach (var item in _launch.Versions)
                Instances.Add(item);

            IsEmpty = Instances.Count == 0;
            StatusText = IsEmpty
                ? Loc.Get(LocKeys.Instances_Empty)
                : Loc.Format(LocKeys.Instances_Count, Instances.Count);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Instances_LoadFailed, ex.Message);
            Debug.WriteLine(ex);
        }
    }

    [RelayCommand]
    private async Task LaunchAsync(GameVersionItem? item)
    {
        if (item is null || IsBusy || _launch.IsLaunching)
            return;

        IsBusy = true;
        IsIndeterminate = true;
        ProgressValue = 0;
        try
        {
            StatusText = Loc.Format(LocKeys.Home_Preparing, item.Id);
            await _launch.LaunchVersionAsync(item).ConfigureAwait(true);
            StatusText = _launch.StatusText;
            ProgressValue = _launch.ProgressValue;
            IsIndeterminate = _launch.IsIndeterminate;
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Home_LaunchFailed, ex.Message);
            Debug.WriteLine(ex);
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
        }
    }

    [RelayCommand]
    private void OpenSettings(GameVersionItem? item)
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
            StatusText = Loc.Format(LocKeys.Instances_OpenedFolder, item.Id);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Download_CannotOpenFolder, ex.Message);
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
            StatusText = Loc.Get(LocKeys.Instances_DeleteNoUi);
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
        StatusText = Loc.Format(LocKeys.Instances_Deleting, item.Id);
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

            Refresh();
            StatusText = Loc.Format(LocKeys.Instances_Deleted, item.Id);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Instances_DeleteFailed, item.Id, ex.Message);
            Debug.WriteLine(ex);
            Refresh();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnLaunchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsBusy)
            return;

        if (e.PropertyName is nameof(LaunchViewModel.StatusText))
            StatusText = _launch.StatusText;
        else if (e.PropertyName is nameof(LaunchViewModel.ProgressValue))
            ProgressValue = _launch.ProgressValue;
        else if (e.PropertyName is nameof(LaunchViewModel.IsIndeterminate))
            IsIndeterminate = _launch.IsIndeterminate;
    }
}
