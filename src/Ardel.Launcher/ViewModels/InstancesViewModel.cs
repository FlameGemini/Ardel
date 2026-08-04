using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

public partial class InstancesViewModel : ObservableObject
{
    private readonly LaunchViewModel _launch;
    private readonly AccountStore _accounts;
    private XamlRoot? _xamlRoot;

    public InstancesViewModel(
        LaunchViewModel launch,
        AccountStore accounts)
    {
        _launch = launch;
        _accounts = accounts;
        _launch.Versions.CollectionChanged += OnVersionsChanged;
        SyncFromLaunch();
    }

    /// <summary>Shared launch state (status / progress / cancel) for the page footer.</summary>
    public LaunchViewModel Launch => _launch;

    /// <summary>Live list — same collection as <see cref="LaunchViewModel.Versions"/>.</summary>
    public ObservableCollection<GameVersionItem> Instances => _launch.Versions;

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
            SyncFromLaunch();
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

        var accounts = _accounts;
        if (accounts.GetActive() is null)
        {
            await ShowMessageAsync(
                    Loc.Get(LocKeys.Instances_Title),
                    Loc.Get(LocKeys.Account_NeedLogin))
                .ConfigureAwait(true);
            return;
        }

        var active = accounts.GetActive()!;
        if (active.Kind != AccountKind.Offline)
        {
            await ShowMessageAsync(
                    Loc.Get(LocKeys.Instances_Title),
                    Loc.Get(LocKeys.Account_MicrosoftComingSoon))
                .ConfigureAwait(true);
            return;
        }

        var nameError = NameRules.ValidatePlayerName(active.DisplayName);
        if (nameError is not null)
        {
            await ShowMessageAsync(Loc.Get(LocKeys.Instances_Title), nameError)
                .ConfigureAwait(true);
            return;
        }

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

    private void OnVersionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        SyncFromLaunch();

    private void SyncFromLaunch()
    {
        IsEmpty = _launch.Versions.Count == 0;
        if (_launch.IsLaunching || _launch.IsGameRunning)
            return;

        _launch.StatusText = IsEmpty
            ? Loc.Get(LocKeys.Home_GoDownload)
            : Loc.Get(LocKeys.Home_Ready);
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
