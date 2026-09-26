using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.Services.Auth;

namespace Ardel.Launcher.ViewModels;

public partial class AccountViewModel : ObservableObject
{
    private readonly AccountStore _accounts;
    private readonly SkinLibraryStore _skins;
    private readonly LaunchViewModel _launch;
    private readonly MicrosoftAuthService _microsoftAuth;

    public AccountViewModel(
        AccountStore accounts,
        SkinLibraryStore skins,
        LaunchViewModel launch,
        MicrosoftAuthService microsoftAuth)
    {
        _accounts = accounts;
        _skins = skins;
        _launch = launch;
        _microsoftAuth = microsoftAuth;
    }

    public ObservableCollection<AccountItemViewModel> Items { get; } = [];
    public ObservableCollection<CapeItemViewModel> Capes { get; } = [];

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAccount))]
    [NotifyPropertyChangedFor(nameof(IsSelectedAccountMicrosoft))]
    [NotifyPropertyChangedFor(nameof(SelectedAccountActiveButtonText))]
    [NotifyPropertyChangedFor(nameof(SelectedAccountSkinDescription))]
    [NotifyPropertyChangedFor(nameof(CanActivateSelectedAccount))]
    private AccountItemViewModel? _selectedAccount;

    public bool HasSelectedAccount => SelectedAccount is not null;
    public bool IsSelectedAccountMicrosoft => SelectedAccount?.Kind == AccountKind.Microsoft;
    public bool HasLicensedAccount => _accounts.HasLicensedAccount;
    public bool CanActivateSelectedAccount => SelectedAccount is not null && !SelectedAccount.IsLocked;

    public string SelectedAccountActiveButtonText => SelectedAccount?.IsActive == true
        ? Loc.Get(LocKeys.Account_Deactivate)
        : Loc.Get(LocKeys.Account_SetActive);

    public string SelectedAccountSkinDescription => IsSelectedAccountMicrosoft
        ? Loc.Get(LocKeys.Account_SkinDescMicrosoft)
        : Loc.Get(LocKeys.Account_SkinDescOffline);

    [ObservableProperty] private bool _isClassicArmModel = true;
    [ObservableProperty] private bool _isSlimArmModel;
    [ObservableProperty] private bool _isCapesLoading;
    [ObservableProperty] private bool _hasNoCapes;

    partial void OnSelectedAccountChanged(AccountItemViewModel? oldValue, AccountItemViewModel? newValue)
    {
        if (newValue is not null)
        {
            var skin = _skins.Find(newValue.SkinId);
            if (skin?.ArmModel == SkinArmModel.Slim)
            {
                IsSlimArmModel = true;
                IsClassicArmModel = false;
            }
            else
            {
                IsClassicArmModel = true;
                IsSlimArmModel = false;
            }

            if (newValue.Kind == AccountKind.Microsoft)
            {
                _ = LoadCapesForSelectedAccountAsync();
            }
            else
            {
                Capes.Clear();
                HasNoCapes = false;
                IsCapesLoading = false;
            }
        }
        else
        {
            Capes.Clear();
            HasNoCapes = false;
            IsCapesLoading = false;
        }
    }

    public async Task RefreshAsync(bool reloadAvatars = true)
    {
        await _skins.EnsureReadyAsync().ConfigureAwait(true);
        await EnsureMicrosoftSkinsAsync().ConfigureAwait(true);

        var activeId = _accounts.ActiveAccountId;
        var accounts = _accounts.Accounts;
        var hasLicensed = _accounts.HasLicensedAccount;
        var prevSelectedId = SelectedAccount?.Id;

        if (!reloadAvatars && Items.Count == accounts.Count)
        {
            foreach (var item in Items)
            {
                var selected = string.Equals(item.Id, activeId, StringComparison.OrdinalIgnoreCase);
                if (item.IsActive != selected)
                    item.IsActive = selected;

                var locked = item.Kind == AccountKind.Offline && !hasLicensed;
                if (item.IsLocked != locked)
                    item.IsLocked = locked;
            }

            IsEmpty = Items.Count == 0;
            ApplyActiveToLaunch();
            OnPropertyChanged(nameof(SelectedAccountActiveButtonText));
            OnPropertyChanged(nameof(CanActivateSelectedAccount));
            return;
        }

        var list = accounts
            .Select(a => new AccountItemViewModel(
                a,
                _skins,
                string.Equals(a.Id, activeId, StringComparison.OrdinalIgnoreCase),
                isLocked: a.Kind == AccountKind.Offline && !hasLicensed))
            .ToList();

        Items.Clear();
        foreach (var item in list)
            Items.Add(item);

        IsEmpty = Items.Count == 0;
        ApplyActiveToLaunch();

        if (reloadAvatars && Items.Count > 0)
        {
            var tasks = Items.Select(item => item.LoadAvatarAsync());
            await Task.WhenAll(tasks).ConfigureAwait(true);
        }

        if (!string.IsNullOrWhiteSpace(prevSelectedId))
        {
            SelectedAccount = Items.FirstOrDefault(i => string.Equals(i.Id, prevSelectedId, StringComparison.OrdinalIgnoreCase))
                ?? Items.FirstOrDefault(i => i.IsActive)
                ?? Items.FirstOrDefault();
        }
        else
        {
            SelectedAccount = Items.FirstOrDefault(i => i.IsActive) ?? Items.FirstOrDefault();
        }

        OnPropertyChanged(nameof(SelectedAccountActiveButtonText));
    }

    private readonly Dictionary<string, (DateTimeOffset Timestamp, IReadOnlyList<MojangCape> Capes)> _capeCache = new();
    private CancellationTokenSource? _capeCts;

    public async Task LoadCapesForSelectedAccountAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        _capeCts?.Cancel();
        _capeCts?.Dispose();
        _capeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _capeCts.Token;

        if (SelectedAccount is null || SelectedAccount.Kind != AccountKind.Microsoft)
        {
            Capes.Clear();
            HasNoCapes = false;
            IsCapesLoading = false;
            return;
        }

        var accountId = SelectedAccount.Id;

        // If cached and not expired, display cache immediately
        if (!force && _capeCache.TryGetValue(accountId, out var cached) &&
            DateTimeOffset.UtcNow - cached.Timestamp < TimeSpan.FromHours(1))
        {
            PopulateCapes(cached.Capes, token);
            return;
        }

        IsCapesLoading = true;

        try
        {
            var rawCapes = await GetCapesAsync(accountId, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || SelectedAccount?.Id != accountId)
                return;

            _capeCache[accountId] = (DateTimeOffset.UtcNow, rawCapes);
            PopulateCapes(rawCapes, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AccountViewModel] LoadCapes error: {ex.Message}");
            // If we have cached capes, keep them!
            if (_capeCache.TryGetValue(accountId, out var fallback))
            {
                PopulateCapes(fallback.Capes, token);
            }
            else if (Capes.Count == 0)
            {
                // Only show empty state if not a rate limit error
                if (!ex.Message.Contains("MojangRateLimit", StringComparison.OrdinalIgnoreCase))
                {
                    HasNoCapes = true;
                }
            }
        }
        finally
        {
            if (SelectedAccount?.Id == accountId)
            {
                IsCapesLoading = false;
            }
        }
    }

    private void PopulateCapes(IReadOnlyList<MojangCape> rawCapes, CancellationToken token)
    {
        var distinctCapes = rawCapes
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (distinctCapes.Count == 0)
        {
            Capes.Clear();
            HasNoCapes = true;
            return;
        }

        HasNoCapes = false;
        var list = new List<CapeItemViewModel>();
        foreach (var cape in distinctCapes)
        {
            var item = new CapeItemViewModel
            {
                Id = cape.Id,
                Alias = cape.Alias,
                Url = cape.Url,
                IsCurrentlyActive = cape.IsActive
            };
            list.Add(item);
        }

        Capes.Clear();
        foreach (var item in list)
        {
            Capes.Add(item);
        }

        _ = Task.Run(async () =>
        {
            foreach (var cape in list)
            {
                if (token.IsCancellationRequested)
                    break;

                var bytes = await CapePreviewHelper.GetCroppedCapePngBytesAsync(cape.Url, token).ConfigureAwait(false);
                if (bytes is not null && bytes.Length > 0 && !token.IsCancellationRequested)
                {
                    var dq = App.MainWindowInstance?.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                    dq?.TryEnqueue(async () =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            cape.DisplayImage = await Skin3DHeadHelper.BitmapImageFromPngAsync(bytes);
                        }
                    });
                }
            }
        }, token);
    }

    public async Task EquipSelectedAccountCapeAsync(string? capeId, CancellationToken cancellationToken = default)
    {
        if (SelectedAccount is null || SelectedAccount.Kind != AccountKind.Microsoft)
            return;

        var accountId = SelectedAccount.Id;
        await SetActiveCapeAsync(accountId, capeId, cancellationToken).ConfigureAwait(true);

        foreach (var cape in Capes)
        {
            cape.IsCurrentlyActive = !string.IsNullOrWhiteSpace(capeId) && string.Equals(cape.Id, capeId, StringComparison.OrdinalIgnoreCase);
        }

        if (_capeCache.TryGetValue(accountId, out var cached))
        {
            var updated = cached.Capes.Select(c =>
            {
                var active = !string.IsNullOrWhiteSpace(capeId) && string.Equals(c.Id, capeId, StringComparison.OrdinalIgnoreCase);
                return new MojangCape(c.Id, c.Alias, c.Url, active);
            }).ToList();
            _capeCache[accountId] = (DateTimeOffset.UtcNow, updated);
        }
    }

    /// <summary>Refresh computed labels after <see cref="Loc.SetLanguage"/>.</summary>
    public void Relocalize()
    {
        foreach (var item in Items)
            item.NotifyLocalization();
        OnPropertyChanged(nameof(SelectedAccountActiveButtonText));
        OnPropertyChanged(nameof(SelectedAccountSkinDescription));
    }

    /// <summary>Sign in with an account (set active session).</summary>
    [RelayCommand]
    private void SelectAccount(AccountItemViewModel? item)
    {
        if (item is null)
            return;

        if (item.IsLocked)
        {
            StatusText = Loc.Get(LocKeys.Account_OfflineLockedHint);
            return;
        }

        if (item.Kind == AccountKind.Offline)
        {
            var nameError = NameRules.ValidatePlayerName(item.DisplayName);
            if (nameError is not null)
            {
                StatusText = nameError;
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(item.MicrosoftAccountId))
        {
            StatusText = Loc.Get(LocKeys.Account_MicrosoftRefreshFailed);
            return;
        }

        if (string.Equals(_accounts.ActiveAccountId, item.Id, StringComparison.OrdinalIgnoreCase))
        {
            _accounts.ClearActive();
            ApplyActiveToLaunch();
            StatusText = Loc.Format(LocKeys.Account_LoggedOut, item.DisplayName);
            foreach (var a in Items)
                a.IsActive = false;
            OnPropertyChanged(nameof(SelectedAccountActiveButtonText));
            return;
        }

        _accounts.SetActive(item.Id);
        ApplyActiveToLaunch();
        StatusText = Loc.Format(LocKeys.Account_LoggedIn, item.DisplayName);
        foreach (var a in Items)
            a.IsActive = string.Equals(a.Id, item.Id, StringComparison.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(SelectedAccountActiveButtonText));
    }

    public void PersistOrder()
    {
        if (Items.Count == 0)
            return;

        _accounts.Reorder(Items.Select(i => i.Id).ToList());
    }

    public AccountRecord CreateOfflineAccount(string name, string? skinId)
    {
        if (!_accounts.HasLicensedAccount)
            throw new InvalidOperationException(Loc.Get(LocKeys.Account_OfflineRequiresLicensedAccount));

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

    public async Task<SkinRecord?> SyncMicrosoftSkinAsync(
        string uuid,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (skinUrl, armModel, _) = await MicrosoftAuthService
                .FetchMojangProfileAsync(uuid, cancellationToken)
                .ConfigureAwait(false);

            return await _skins
                .EnsureOfficialSkinAsync(uuid, playerName, skinUrl, armModel, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AccountViewModel] SyncMicrosoftSkinAsync failed: {ex.Message}");
            return null;
        }
    }

    public async Task RefreshMicrosoftAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var existing = _accounts.Find(accountId)
                       ?? throw new InvalidOperationException("Account not found.");
        if (existing.Kind != AccountKind.Microsoft || string.IsNullOrWhiteSpace(existing.Uuid))
            return;

        var (skinUrl, armModel, mojPlayerName) = await MicrosoftAuthService
            .FetchMojangProfileAsync(existing.Uuid, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(mojPlayerName) && !string.Equals(existing.DisplayName, mojPlayerName, StringComparison.Ordinal))
        {
            existing.DisplayName = mojPlayerName;
        }

        var skinRecord = await _skins
            .EnsureOfficialSkinAsync(existing.Uuid, existing.DisplayName, skinUrl, armModel, cancellationToken)
            .ConfigureAwait(false);

        if (skinRecord is not null)
        {
            existing.SkinId = skinRecord.Id;
        }

        if (!string.IsNullOrWhiteSpace(existing.MicrosoftAccountId))
        {
            try
            {
                await _microsoftAuth.AuthenticateSilentlyAsync(existing.MicrosoftAccountId, forceRefresh: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountViewModel] Silent auth refresh warning: {ex.Message}");
            }
        }

        _accounts.Update(existing);
        if (string.Equals(_accounts.ActiveAccountId, accountId, StringComparison.OrdinalIgnoreCase))
            ApplyActiveToLaunch();
    }

    public async Task EnsureMicrosoftSkinsAsync(CancellationToken cancellationToken = default)
    {
        var updated = false;
        foreach (var account in _accounts.Accounts.Where(a => a.Kind == AccountKind.Microsoft))
        {
            if (string.IsNullOrWhiteSpace(account.Uuid))
                continue;

            var officialId = $"official-{NormalizeUuid(account.Uuid)}";
            if (!string.Equals(account.SkinId, officialId, StringComparison.OrdinalIgnoreCase))
            {
                account.SkinId = officialId;
                _accounts.Update(account);
                updated = true;
            }

            var officialSkin = _skins.Find(officialId);
            if (officialSkin is null)
            {
                var skin = await SyncMicrosoftSkinAsync(account.Uuid, account.DisplayName, cancellationToken).ConfigureAwait(false);
                if (skin is not null)
                {
                    updated = true;
                }
            }
        }

        if (updated)
        {
            var active = _accounts.GetActive();
            if (active?.Kind == AccountKind.Microsoft)
                ApplyActiveToLaunch();
        }
    }

    public async Task<AccountRecord> CreateMicrosoftAccountAsync(CancellationToken cancellationToken = default)
    {
        var result = await _microsoftAuth.SignInInteractivelyAsync(cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(result.Username) || string.IsNullOrWhiteSpace(result.Uuid))
            throw new InvalidOperationException(Loc.Get(LocKeys.Account_MicrosoftNeedOwn));

        var skinRecord = await SyncMicrosoftSkinAsync(result.Uuid, result.Username, cancellationToken).ConfigureAwait(true);

        var existing = _accounts.Accounts.FirstOrDefault(a =>
            a.Kind == AccountKind.Microsoft &&
            (string.Equals(a.MicrosoftAccountId, result.AccountId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(NormalizeUuid(a.Uuid), result.Uuid, StringComparison.OrdinalIgnoreCase)));

        if (existing is not null)
        {
            existing.DisplayName = result.Username;
            existing.Uuid = result.Uuid;
            existing.MicrosoftAccountId = result.AccountId;
            if (skinRecord is not null && (string.IsNullOrWhiteSpace(existing.SkinId) || existing.SkinId.StartsWith("official-", StringComparison.OrdinalIgnoreCase)))
            {
                existing.SkinId = skinRecord.Id;
            }
            _accounts.Update(existing);
            _accounts.SetActive(existing.Id);
            ApplyActiveToLaunch();
            return existing;
        }

        var record = _accounts.Add(new AccountRecord
        {
            Kind = AccountKind.Microsoft,
            DisplayName = result.Username,
            Uuid = result.Uuid,
            MicrosoftAccountId = result.AccountId,
            SkinId = skinRecord?.Id
        });
        _accounts.SetActive(record.Id);
        ApplyActiveToLaunch();
        return record;
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
        var existing = _accounts.Find(id);
        if (existing?.Kind == AccountKind.Microsoft &&
            !string.IsNullOrWhiteSpace(existing.MicrosoftAccountId))
        {
            _ = _microsoftAuth.SignOutAsync(existing.MicrosoftAccountId);
        }

        _accounts.Delete(id);
        ApplyActiveToLaunch();
        _ = RefreshAsync(reloadAvatars: false);
    }

    public void SetAccountSkin(string accountId, string? skinId)
    {
        var existing = _accounts.Find(accountId)
                       ?? throw new InvalidOperationException("Account not found.");

        if (existing.Kind == AccountKind.Microsoft)
        {
            existing.SkinId = $"official-{NormalizeUuid(existing.Uuid)}";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(skinId))
                throw new InvalidOperationException(Loc.Get(LocKeys.Account_NeedSkin));
            existing.SkinId = skinId;
        }
        _accounts.Update(existing);
    }

    public async Task UploadMicrosoftSkinAsync(
        string accountId,
        byte[] pngBytes,
        SkinArmModel armModel,
        CancellationToken cancellationToken = default)
    {
        var existing = _accounts.Find(accountId)
                       ?? throw new InvalidOperationException("Account not found.");
        if (existing.Kind != AccountKind.Microsoft || string.IsNullOrWhiteSpace(existing.MicrosoftAccountId))
            throw new InvalidOperationException("Not a valid Microsoft account.");

        await _microsoftAuth.UploadSkinAsync(existing.MicrosoftAccountId, pngBytes, armModel, cancellationToken).ConfigureAwait(false);
        await _skins.UpdateOfficialSkinAsync(existing.Uuid, pngBytes, armModel, cancellationToken).ConfigureAwait(false);

        var officialId = $"official-{NormalizeUuid(existing.Uuid)}";
        existing.SkinId = officialId;
        _accounts.Update(existing);

        if (string.Equals(_accounts.ActiveAccountId, accountId, StringComparison.OrdinalIgnoreCase))
            ApplyActiveToLaunch();
    }

    public Task<IReadOnlyList<MojangCape>> GetCapesAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var existing = _accounts.Find(accountId)
                       ?? throw new InvalidOperationException("Account not found.");
        if (existing.Kind != AccountKind.Microsoft || string.IsNullOrWhiteSpace(existing.MicrosoftAccountId))
            return Task.FromResult<IReadOnlyList<MojangCape>>(Array.Empty<MojangCape>());

        return _microsoftAuth.GetCapesAsync(existing.MicrosoftAccountId, cancellationToken);
    }

    public Task SetActiveCapeAsync(
        string accountId,
        string? capeId,
        CancellationToken cancellationToken = default)
    {
        var existing = _accounts.Find(accountId)
                       ?? throw new InvalidOperationException("Account not found.");
        if (existing.Kind != AccountKind.Microsoft || string.IsNullOrWhiteSpace(existing.MicrosoftAccountId))
            throw new InvalidOperationException("Not a valid Microsoft account.");

        return _microsoftAuth.SetActiveCapeAsync(existing.MicrosoftAccountId, capeId, cancellationToken);
    }

    public Task<(bool Allowed, DateTimeOffset? CreatedAt)> CheckNameChangeEligibilityAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var existing = _accounts.Find(accountId)
                       ?? throw new InvalidOperationException("Account not found.");
        if (existing.Kind != AccountKind.Microsoft || string.IsNullOrWhiteSpace(existing.MicrosoftAccountId))
            return Task.FromResult<(bool, DateTimeOffset?)>((false, null));

        return _microsoftAuth.CheckNameChangeEligibilityAsync(existing.MicrosoftAccountId, cancellationToken);
    }

    public Task<(bool Available, string Status)> CheckNameAvailabilityAsync(
        string accountId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var existing = _accounts.Find(accountId)
                       ?? throw new InvalidOperationException("Account not found.");
        if (existing.Kind != AccountKind.Microsoft || string.IsNullOrWhiteSpace(existing.MicrosoftAccountId))
            return Task.FromResult((false, "Invalid account"));

        return _microsoftAuth.CheckNameAvailabilityAsync(existing.MicrosoftAccountId, newName, cancellationToken);
    }

    public async Task<string> ChangeMicrosoftAccountNameAsync(
        string accountId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var existing = _accounts.Find(accountId)
                       ?? throw new InvalidOperationException("Account not found.");
        if (existing.Kind != AccountKind.Microsoft || string.IsNullOrWhiteSpace(existing.MicrosoftAccountId))
            throw new InvalidOperationException("Not a valid Microsoft account.");

        var updatedName = await _microsoftAuth.ChangePlayerNameAsync(existing.MicrosoftAccountId, newName, cancellationToken).ConfigureAwait(false);
        existing.DisplayName = updatedName;
        _accounts.Update(existing);

        if (string.Equals(_accounts.ActiveAccountId, accountId, StringComparison.OrdinalIgnoreCase))
            ApplyActiveToLaunch();

        return updatedName;
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

    public async Task<OnlineLaunchSession> ResolveOnlineSessionAsync(
        AccountRecord account,
        CancellationToken cancellationToken = default)
    {
        if (account.Kind != AccountKind.Microsoft ||
            string.IsNullOrWhiteSpace(account.MicrosoftAccountId))
            throw new InvalidOperationException(Loc.Get(LocKeys.Account_MicrosoftRefreshFailed));

        var session = await _microsoftAuth
            .AuthenticateSilentlyAsync(account.MicrosoftAccountId, cancellationToken)
            .ConfigureAwait(false);

        var username = session.Username ?? account.DisplayName;
        var uuid = NormalizeUuid(session.UUID);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(uuid) ||
            string.IsNullOrWhiteSpace(session.AccessToken))
            throw new InvalidOperationException(Loc.Get(LocKeys.Account_MicrosoftRefreshFailed));

        if (!string.Equals(account.DisplayName, username, StringComparison.Ordinal) ||
            !string.Equals(NormalizeUuid(account.Uuid), uuid, StringComparison.OrdinalIgnoreCase))
        {
            account.DisplayName = username;
            account.Uuid = uuid;
            _accounts.Update(account);
        }

        return new OnlineLaunchSession(username, uuid, session.AccessToken);
    }

    private void ApplyActiveToLaunch()
    {
        var active = _accounts.GetActive();
        if (active is null)
            return;

        _launch.EnsureSettingsReady();
        if (!string.Equals(_launch.PlayerName, active.DisplayName, StringComparison.Ordinal))
            _launch.PlayerName = active.DisplayName;
    }

    private static string NormalizeUuid(string? uuid) =>
        string.IsNullOrWhiteSpace(uuid)
            ? string.Empty
            : uuid.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
}
