using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

public partial class AccountViewModel : ObservableObject
{
    private readonly AccountStore _accounts;
    private readonly SkinLibraryStore _skins;
    private readonly LaunchViewModel _launch;

    public AccountViewModel(
        AccountStore accounts,
        SkinLibraryStore skins,
        LaunchViewModel launch)
    {
        _accounts = accounts;
        _skins = skins;
        _launch = launch;
    }

    public ObservableCollection<AccountItemViewModel> Items { get; } = [];

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _statusText = string.Empty;

    public async Task RefreshAsync()
    {
        await _skins.EnsureReadyAsync().ConfigureAwait(true);

        var activeId = _accounts.ActiveAccountId;
        var list = _accounts.Accounts
            .OrderByDescending(a => string.Equals(a.Id, activeId, StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AccountItemViewModel(
                a,
                _skins,
                string.Equals(a.Id, activeId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Items.Clear();
        foreach (var item in list)
            Items.Add(item);

        IsEmpty = Items.Count == 0;
        ApplyActiveToLaunch();

        foreach (var item in Items)
            await item.LoadAvatarAsync().ConfigureAwait(true);
    }

    /// <summary>Sign in with an account (offline: set active session).</summary>
    [RelayCommand]
    private void SelectAccount(AccountItemViewModel? item)
    {
        if (item is null)
            return;

        if (item.Kind == AccountKind.Microsoft)
        {
            StatusText = Loc.Get(LocKeys.Account_MicrosoftComingSoon);
            return;
        }

        var nameError = NameRules.ValidatePlayerName(item.DisplayName);
        if (nameError is not null)
        {
            StatusText = nameError;
            return;
        }

        if (string.Equals(_accounts.ActiveAccountId, item.Id, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = Loc.Format(LocKeys.Account_LoggedIn, item.DisplayName);
            return;
        }

        _accounts.SetActive(item.Id);
        ApplyActiveToLaunch();
        StatusText = Loc.Format(LocKeys.Account_LoggedIn, item.DisplayName);
        _ = RefreshAsync();
    }

    public AccountRecord CreateOfflineAccount(string name, string? skinId)
    {
        var trimmed = name.Trim();
        var error = NameRules.ValidatePlayerName(trimmed);
        if (error is not null)
            throw new InvalidOperationException(error);

        return _accounts.Add(new AccountRecord
        {
            Kind = AccountKind.Offline,
            DisplayName = trimmed,
            Uuid = OfflinePlayerUuid.FromPlayerName(trimmed),
            SkinId = string.IsNullOrWhiteSpace(skinId)
                ? SkinLibraryStore.BuiltinSteveOfflineId
                : skinId
        });
    }

    public void UpdateOfflineAccountName(string id, string name)
    {
        var existing = _accounts.Find(id)
                       ?? throw new InvalidOperationException("Account not found.");
        if (existing.Kind != AccountKind.Offline)
            throw new InvalidOperationException(Loc.Get(LocKeys.Account_MicrosoftComingSoon));

        var trimmed = name.Trim();
        var error = NameRules.ValidatePlayerName(trimmed);
        if (error is not null)
            throw new InvalidOperationException(error);

        existing.DisplayName = trimmed;
        existing.Uuid = OfflinePlayerUuid.FromPlayerName(trimmed);
        _accounts.Update(existing);
        if (string.Equals(_accounts.ActiveAccountId, id, StringComparison.OrdinalIgnoreCase))
            ApplyActiveToLaunch();
    }

    public void UpdateOfflineAccount(string id, string name, string? skinId)
    {
        var existing = _accounts.Find(id)
                       ?? throw new InvalidOperationException("Account not found.");
        if (existing.Kind != AccountKind.Offline)
            throw new InvalidOperationException(Loc.Get(LocKeys.Account_MicrosoftComingSoon));

        var trimmed = name.Trim();
        var error = NameRules.ValidatePlayerName(trimmed);
        if (error is not null)
            throw new InvalidOperationException(error);

        existing.DisplayName = trimmed;
        if (!string.IsNullOrWhiteSpace(skinId))
            existing.SkinId = skinId;
        existing.Uuid = OfflinePlayerUuid.FromPlayerName(trimmed);
        _accounts.Update(existing);
        if (string.Equals(_accounts.ActiveAccountId, id, StringComparison.OrdinalIgnoreCase))
            ApplyActiveToLaunch();
    }

    public void DeleteAccount(string id)
    {
        _accounts.Delete(id);
        ApplyActiveToLaunch();
    }

    public void SetAccountSkin(string accountId, string? skinId)
    {
        if (string.IsNullOrWhiteSpace(skinId))
            throw new InvalidOperationException(Loc.Get(LocKeys.Account_NeedSkin));

        var existing = _accounts.Find(accountId)
                       ?? throw new InvalidOperationException("Account not found.");
        existing.SkinId = skinId;
        _accounts.Update(existing);
    }

    public async Task<IReadOnlyList<SkinRecord>> SkinsForAsync(AccountKind kind)
    {
        await _skins.EnsureReadyAsync().ConfigureAwait(true);
        return _skins.List(kind == AccountKind.Microsoft
            ? SkinLibraryKind.Microsoft
            : SkinLibraryKind.Offline);
    }

    public IReadOnlyList<SkinRecord> SkinsFor(AccountKind kind) =>
        _skins.List(kind == AccountKind.Microsoft
            ? SkinLibraryKind.Microsoft
            : SkinLibraryKind.Offline);

    private void ApplyActiveToLaunch()
    {
        var active = _accounts.GetActive();
        if (active is null || active.Kind != AccountKind.Offline)
            return;

        _launch.EnsureSettingsReady();
        if (!string.Equals(_launch.PlayerName, active.DisplayName, StringComparison.Ordinal))
            _launch.PlayerName = active.DisplayName;
    }
}
