using System.Diagnostics;
using System.Text.Json;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>Persists account profiles under %LocalAppData%\Ardel\accounts.json.</summary>
public sealed class AccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly object _gate = new();
    private AccountsDocument _doc = new();

    public AccountStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "accounts.json");
        Reload();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<AccountRecord> Accounts
    {
        get
        {
            lock (_gate)
                return _doc.Accounts.ToList();
        }
    }

    public string? ActiveAccountId
    {
        get
        {
            lock (_gate)
                return _doc.ActiveAccountId;
        }
    }

    public AccountRecord? GetActive()
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(_doc.ActiveAccountId))
                return null;
            return _doc.Accounts.FirstOrDefault(a =>
                string.Equals(a.Id, _doc.ActiveAccountId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public AccountRecord? Find(string id)
    {
        lock (_gate)
            return _doc.Accounts.FirstOrDefault(a =>
                string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void Reload()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    _doc = CreateDefault();
                    WriteUnlocked(_doc);
                }
                else
                {
                    var json = File.ReadAllText(_path);
                    _doc = JsonSerializer.Deserialize<AccountsDocument>(json, JsonOptions) ?? CreateDefault();
                    NormalizeUnlocked();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AccountStore] Load failed: {ex.Message}");
                _doc = CreateDefault();
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public AccountRecord Add(AccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);
        lock (_gate)
        {
            if (account.Kind == AccountKind.Offline)
                account.Uuid = OfflinePlayerUuid.FromPlayerName(account.DisplayName.Trim());

            _doc.Accounts.Add(account);
            WriteUnlocked(_doc);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return account;
    }

    public void Update(AccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);
        lock (_gate)
        {
            var index = _doc.Accounts.FindIndex(a =>
                string.Equals(a.Id, account.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException("Account not found.");

            if (account.Kind == AccountKind.Offline)
                account.Uuid = OfflinePlayerUuid.FromPlayerName(account.DisplayName.Trim());

            _doc.Accounts[index] = account;
            WriteUnlocked(_doc);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            _doc.Accounts.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_doc.ActiveAccountId, id, StringComparison.OrdinalIgnoreCase))
                _doc.ActiveAccountId = null;
            WriteUnlocked(_doc);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool HasActiveSession => !string.IsNullOrEmpty(ActiveAccountId) && GetActive() is not null;

    public void SetActive(string id)
    {
        lock (_gate)
        {
            if (!_doc.Accounts.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Account not found.");
            _doc.ActiveAccountId = id;
            WriteUnlocked(_doc);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ClearActive()
    {
        lock (_gate)
        {
            _doc.ActiveAccountId = null;
            WriteUnlocked(_doc);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void NormalizeUnlocked()
    {
        foreach (var account in _doc.Accounts)
        {
            if (account.Kind == AccountKind.Offline &&
                !string.IsNullOrWhiteSpace(account.DisplayName) &&
                NameRules.ValidatePlayerName(account.DisplayName) is null)
            {
                account.Uuid = OfflinePlayerUuid.FromPlayerName(account.DisplayName.Trim());
            }

            // Collapse legacy Custom 1/2 slot ids → single Custom.
            if (string.Equals(account.SkinId, "custom-1-offline", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(account.SkinId, "custom-2-offline", StringComparison.OrdinalIgnoreCase))
                account.SkinId = SkinLibraryStore.CustomOfflineId;
            else if (string.Equals(account.SkinId, "custom-1-microsoft", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(account.SkinId, "custom-2-microsoft", StringComparison.OrdinalIgnoreCase))
                account.SkinId = SkinLibraryStore.CustomMicrosoftId;
        }

        // Active session is explicit login only — never invent one on load.
        if (string.IsNullOrEmpty(_doc.ActiveAccountId) ||
            !_doc.Accounts.Any(a =>
                string.Equals(a.Id, _doc.ActiveAccountId, StringComparison.OrdinalIgnoreCase)))
        {
            _doc.ActiveAccountId = null;
        }

        WriteUnlocked(_doc);
    }

    private void WriteUnlocked(AccountsDocument doc)
    {
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        File.WriteAllText(_path, json);
    }

    private static AccountsDocument CreateDefault() => new()
    {
        ActiveAccountId = null,
        Accounts = []
    };
}
