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
using Ardel.Launcher.Views;

namespace Ardel.Launcher.ViewModels;

public partial class InstancesViewModel : ObservableObject
{
    private readonly LaunchViewModel _launch;
    private readonly AccountStore _accounts;
    private XamlRoot? _xamlRoot;
    private string? _pendingLaunchVersionId;

    public InstancesViewModel(
        LaunchViewModel launch,
        AccountStore accounts)
    {
        _launch = launch;
        _accounts = accounts;
        _launch.Versions.CollectionChanged += OnVersionsChanged;
        _launch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LaunchViewModel.IsLocalReady))
            {
                SyncFromLaunch();
                _ = TryConsumePendingLaunchAsync();
            }
            else if (e.PropertyName == nameof(LaunchViewModel.IsLaunching))
            {
                IsBusy = _launch.IsLaunching;
                OnPropertyChanged(nameof(CanStartLaunch));
            }
        };
        SyncFromLaunch();
        IsBusy = _launch.IsLaunching;
    }

    /// <summary>Queue a version to launch after navigating to this page (e.g. Home quick launch).</summary>
    public void QueueLaunch(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return;
        _pendingLaunchVersionId = versionId.Trim();
    }

    /// <summary>False while a launch is in progress — blocks starting another.</summary>
    public bool CanStartLaunch => !_launch.IsLaunching;

    /// <summary>Shared launch state (status / progress / cancel) for the page footer.</summary>
    public LaunchViewModel Launch => _launch;

    /// <summary>Live list — same collection as <see cref="LaunchViewModel.Versions"/>.</summary>
    public ObservableCollection<GameVersionItem> Instances => _launch.Versions;

    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isBusy;

    public void AttachXamlRoot(XamlRoot? root)
    {
        if (root is not null)
            _xamlRoot = root;
    }

    [RelayCommand]
    private void GoDownload()
    {
        if (App.MainWindowInstance is MainWindow mainWindow)
            mainWindow.NavigateToDownload();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            // Avoid a full disk rescan on every page visit — only when first load or forced.
            if (_launch.IsLocalReady && Instances.Count > 0)
            {
                SyncFromLaunch();
                await TryConsumePendingLaunchAsync().ConfigureAwait(true);
                return;
            }

            await _launch.LoadLocalVersionsAsync().ConfigureAwait(true);
            SyncFromLaunch();
            await TryConsumePendingLaunchAsync().ConfigureAwait(true);
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

    private async Task TryConsumePendingLaunchAsync()
    {
        var id = _pendingLaunchVersionId;
        if (string.IsNullOrEmpty(id) || !_launch.IsLocalReady || _launch.IsLaunching)
            return;

        _pendingLaunchVersionId = null;
        var item = Instances.FirstOrDefault(v =>
            string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        await LaunchAsync(item).ConfigureAwait(true);
    }

    public void PersistOrder()
    {
        if (Instances.Count == 0)
            return;
        _launch.PersistInstanceOrder();
    }

    [RelayCommand]
    private async Task LaunchAsync(GameVersionItem? item)
    {
        if (item is null || _launch.IsLaunching)
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
        if (active.Kind == AccountKind.Offline)
        {
            var nameError = NameRules.ValidatePlayerName(active.DisplayName);
            if (nameError is not null)
            {
                await ShowMessageAsync(Loc.Get(LocKeys.Instances_Title), nameError)
                    .ConfigureAwait(true);
                return;
            }
        }
        else if (active.Kind != AccountKind.Microsoft ||
                 string.IsNullOrWhiteSpace(active.MicrosoftAccountId))
        {
            await ShowMessageAsync(
                    Loc.Get(LocKeys.Instances_Title),
                    Loc.Get(LocKeys.Account_MicrosoftRefreshFailed))
                .ConfigureAwait(true);
            return;
        }

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
    }

    private void OnVersionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        SyncFromLaunch();

    private void SyncFromLaunch()
    {
        IsLoading = !_launch.IsLocalReady;
        IsEmpty = _launch.IsLocalReady && _launch.Versions.Count == 0;
        if (_launch.IsLaunching || _launch.IsGameRunning)
            return;

        if (IsLoading)
            _launch.StatusText = Loc.Get(LocKeys.Instances_Loading);
        else
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
            CloseButtonText = Loc.Get(LocKeys.Action_Close)
        };
        await dialog.ShowAsync();
    }
}
