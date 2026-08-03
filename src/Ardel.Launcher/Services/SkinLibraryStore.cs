using System.Diagnostics;
using System.Text.Json;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>Skin PNG library under %LocalAppData%\Ardel\skins\.</summary>
public sealed class SkinLibraryStore
{
    public const string BuiltinSteveOfflineId = "builtin-steve-offline";
    public const string BuiltinAlexOfflineId = "builtin-alex-offline";
    public const string BuiltinSteveMicrosoftId = "builtin-steve-microsoft";
    public const string BuiltinAlexMicrosoftId = "builtin-alex-microsoft";
    public const string CustomOfflineId = "custom-offline";
    public const string CustomMicrosoftId = "custom-microsoft";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _root;
    private readonly string _indexPath;
    private readonly object _gate = new();
    private SkinsDocument _doc = new();
    private Task? _ensureBuiltInsTask;

    public SkinLibraryStore()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            "skins");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "offline"));
        Directory.CreateDirectory(Path.Combine(_root, "microsoft"));
        _indexPath = Path.Combine(_root, "skins.json");
        Reload();
        _ensureBuiltInsTask = EnsureBuiltInsAsync();
    }

    public event EventHandler? Changed;

    public string RootDirectory => _root;

    public Task EnsureReadyAsync() => _ensureBuiltInsTask ?? Task.CompletedTask;

    public IReadOnlyList<SkinRecord> Skins
    {
        get
        {
            lock (_gate)
                return _doc.Skins.ToList();
        }
    }

    public IReadOnlyList<SkinRecord> List(SkinLibraryKind library)
    {
        lock (_gate)
            return _doc.Skins.Where(s => s.Library == library).ToList();
    }

    public SkinRecord? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        lock (_gate)
            return _doc.Skins.FirstOrDefault(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public string GetAbsolutePath(SkinRecord skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        return Path.Combine(_root, LibraryFolder(skin.Library), skin.FileName);
    }

    public void Reload()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_indexPath))
                {
                    _doc = new SkinsDocument();
                    WriteUnlocked(_doc);
                }
                else
                {
                    var json = File.ReadAllText(_indexPath);
                    _doc = JsonSerializer.Deserialize<SkinsDocument>(json, JsonOptions) ?? new SkinsDocument();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SkinLibraryStore] Load failed: {ex.Message}");
                _doc = new SkinsDocument();
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task EnsureBuiltInsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureOneAsync(
                    BuiltinSteveOfflineId,
                    "Steve",
                    SkinLibraryKind.Offline,
                    SkinArmModel.Classic,
                    isBuiltIn: true,
                    isCustomSlot: false,
                    configured: true,
                    forceRewrite: true,
                    BuiltInSkinGenerator.WriteSteveAsync,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureOneAsync(
                    BuiltinAlexOfflineId,
                    "Alex",
                    SkinLibraryKind.Offline,
                    SkinArmModel.Slim,
                    isBuiltIn: true,
                    isCustomSlot: false,
                    configured: true,
                    forceRewrite: true,
                    BuiltInSkinGenerator.WriteAlexAsync,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureOneAsync(
                    BuiltinSteveMicrosoftId,
                    "Steve",
                    SkinLibraryKind.Microsoft,
                    SkinArmModel.Classic,
                    isBuiltIn: true,
                    isCustomSlot: false,
                    configured: true,
                    forceRewrite: true,
                    BuiltInSkinGenerator.WriteSteveAsync,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureOneAsync(
                    BuiltinAlexMicrosoftId,
                    "Alex",
                    SkinLibraryKind.Microsoft,
                    SkinArmModel.Slim,
                    isBuiltIn: true,
                    isCustomSlot: false,
                    configured: true,
                    forceRewrite: true,
                    BuiltInSkinGenerator.WriteAlexAsync,
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureCustomSlotAsync(
                    CustomOfflineId,
                    Loc.Get(LocKeys.Account_SkinCustom),
                    SkinLibraryKind.Offline,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureCustomSlotAsync(
                    CustomMicrosoftId,
                    Loc.Get(LocKeys.Account_SkinCustom),
                    SkinLibraryKind.Microsoft,
                    cancellationToken)
                .ConfigureAwait(false);

            MigrateLegacyCustomSlotsUnlocked();

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinLibraryStore] Built-in seed failed: {ex.Message}");
        }
    }

    private async Task EnsureCustomSlotAsync(
        string id,
        string name,
        SkinLibraryKind library,
        CancellationToken cancellationToken)
    {
        bool configured;
        lock (_gate)
        {
            configured = _doc.Skins.FirstOrDefault(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))?.IsConfigured == true;
        }

        await EnsureOneAsync(
                id,
                name,
                library,
                SkinArmModel.Classic,
                isBuiltIn: false,
                isCustomSlot: true,
                configured: configured,
                forceRewrite: false,
                BuiltInSkinGenerator.WriteEmptyAsync,
                cancellationToken,
                keepExistingFile: true)
            .ConfigureAwait(false);
    }

    private async Task EnsureOneAsync(
        string id,
        string name,
        SkinLibraryKind library,
        SkinArmModel model,
        bool isBuiltIn,
        bool isCustomSlot,
        bool configured,
        bool forceRewrite,
        Func<string, CancellationToken, Task> writer,
        CancellationToken cancellationToken,
        bool keepExistingFile = false)
    {
        var fileName = id + ".png";
        var path = Path.Combine(_root, LibraryFolder(library), fileName);
        var exists = File.Exists(path) && LooksLikePng(path) && new FileInfo(path).Length >= 64;
        var shouldWrite = !exists || (forceRewrite && !keepExistingFile);
        if (keepExistingFile && exists)
            shouldWrite = false;
        if (shouldWrite)
            await writer(path, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            var existing = _doc.Skins.FirstOrDefault(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _doc.Skins.Add(new SkinRecord
                {
                    Id = id,
                    Name = name,
                    Library = library,
                    ArmModel = model,
                    FileName = fileName,
                    IsBuiltIn = isBuiltIn,
                    IsCustomSlot = isCustomSlot,
                    IsConfigured = isCustomSlot ? (exists && !shouldWrite && File.Exists(path) && new FileInfo(path).Length > 400) : configured
                });
            }
            else
            {
                existing.IsBuiltIn = isBuiltIn;
                existing.IsCustomSlot = isCustomSlot;
                if (!isCustomSlot || string.IsNullOrWhiteSpace(existing.Name))
                    existing.Name = name;
                if (!isCustomSlot)
                    existing.ArmModel = model;
                existing.FileName = fileName;
                existing.Library = library;
                if (!isCustomSlot)
                    existing.IsConfigured = true;
                else if (configured)
                    existing.IsConfigured = true;
            }

            WriteUnlocked(_doc);
        }
    }

    public void SetSlotArmModel(string slotId, SkinArmModel armModel)
    {
        lock (_gate)
        {
            var slot = _doc.Skins.FirstOrDefault(s =>
                           string.Equals(s.Id, slotId, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("Skin slot not found.");
            if (!slot.IsCustomSlot)
                throw new InvalidOperationException("Not a custom skin slot.");

            slot.ArmModel = armModel;
            WriteUnlocked(_doc);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Overwrite a fixed custom slot with a user PNG.</summary>
    public async Task<SkinRecord> ReplaceSlotAsync(
        string slotId,
        string sourcePngPath,
        SkinArmModel armModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePngPath);
        if (!File.Exists(sourcePngPath))
            throw new FileNotFoundException("Skin file not found.", sourcePngPath);

        SkinRecord slot;
        lock (_gate)
        {
            slot = _doc.Skins.FirstOrDefault(s =>
                       string.Equals(s.Id, slotId, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("Skin slot not found.");
            if (!slot.IsCustomSlot)
                throw new InvalidOperationException("Not a custom skin slot.");
        }

        var dest = GetAbsolutePath(slot);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await using (var input = File.OpenRead(sourcePngPath))
        await using (var output = File.Create(dest))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            slot.ArmModel = armModel;
            slot.IsConfigured = true;
            slot.Name = Loc.Get(LocKeys.Account_SkinCustom);
            WriteUnlocked(_doc);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return slot;
    }

    public IReadOnlyList<SkinRecord> ListPickerOptions(SkinLibraryKind library)
    {
        string[] order = library == SkinLibraryKind.Microsoft
            ? [BuiltinSteveMicrosoftId, BuiltinAlexMicrosoftId, CustomMicrosoftId]
            : [BuiltinSteveOfflineId, BuiltinAlexOfflineId, CustomOfflineId];

        lock (_gate)
        {
            var list = new List<SkinRecord>(3);
            foreach (var id in order)
            {
                var skin = _doc.Skins.FirstOrDefault(s =>
                    string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
                if (skin is not null)
                    list.Add(skin);
            }

            return list;
        }
    }

    private void MigrateLegacyCustomSlotsUnlocked()
    {
        lock (_gate)
        {
            // Prefer an already-filled legacy slot when collapsing Custom 1/2 → Custom.
            TryPromoteLegacy("custom-1-offline", CustomOfflineId, SkinLibraryKind.Offline);
            TryPromoteLegacy("custom-2-offline", CustomOfflineId, SkinLibraryKind.Offline);
            TryPromoteLegacy("custom-1-microsoft", CustomMicrosoftId, SkinLibraryKind.Microsoft);
            TryPromoteLegacy("custom-2-microsoft", CustomMicrosoftId, SkinLibraryKind.Microsoft);

            _doc.Skins.RemoveAll(s =>
                s.Id.StartsWith("custom-1-", StringComparison.OrdinalIgnoreCase) ||
                s.Id.StartsWith("custom-2-", StringComparison.OrdinalIgnoreCase));
            WriteUnlocked(_doc);
        }
    }

    private void TryPromoteLegacy(string legacyId, string newId, SkinLibraryKind library)
    {
        var legacy = _doc.Skins.FirstOrDefault(s =>
            string.Equals(s.Id, legacyId, StringComparison.OrdinalIgnoreCase));
        var target = _doc.Skins.FirstOrDefault(s =>
            string.Equals(s.Id, newId, StringComparison.OrdinalIgnoreCase));
        if (legacy is null || target is null || !legacy.IsConfigured || target.IsConfigured)
            return;

        var src = Path.Combine(_root, LibraryFolder(library), legacy.FileName);
        var dst = Path.Combine(_root, LibraryFolder(library), target.FileName);
        try
        {
            if (File.Exists(src))
                File.Copy(src, dst, overwrite: true);
            target.IsConfigured = true;
            target.ArmModel = legacy.ArmModel;
            target.Name = Loc.Get(LocKeys.Account_SkinCustom);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinLibraryStore] Legacy migrate failed: {ex.Message}");
        }
    }

    public async Task<SkinRecord> ImportAsync(
        string displayName,
        SkinLibraryKind library,
        SkinArmModel armModel,
        string sourcePngPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePngPath);

        var nameError = NameRules.ValidateSkinName(displayName);
        if (nameError is not null)
            throw new InvalidOperationException(nameError);

        if (!File.Exists(sourcePngPath))
            throw new FileNotFoundException("Skin file not found.", sourcePngPath);

        var id = Guid.NewGuid().ToString("N");
        var fileName = id + ".png";
        var destDir = Path.Combine(_root, LibraryFolder(library));
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, fileName);

        await using (var input = File.OpenRead(sourcePngPath))
        await using (var output = File.Create(dest))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        var record = new SkinRecord
        {
            Id = id,
            Name = displayName.Trim(),
            Library = library,
            ArmModel = armModel,
            FileName = fileName,
            IsBuiltIn = false
        };

        lock (_gate)
        {
            _doc.Skins.Insert(0, record);
            WriteUnlocked(_doc);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return record;
    }

    public void Delete(string id)
    {
        SkinRecord? removed = null;
        lock (_gate)
        {
            removed = _doc.Skins.FirstOrDefault(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removed is null)
                return;
            if (removed.IsBuiltIn || removed.IsCustomSlot)
                throw new InvalidOperationException(Loc.Get(LocKeys.Skin_CannotDeleteBuiltIn));

            _doc.Skins.Remove(removed);
            WriteUnlocked(_doc);
        }

        try
        {
            var path = GetAbsolutePath(removed);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinLibraryStore] Delete file failed: {ex.Message}");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void WriteUnlocked(SkinsDocument doc)
    {
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        File.WriteAllText(_indexPath, json);
    }

    private static string LibraryFolder(SkinLibraryKind library) =>
        library == SkinLibraryKind.Microsoft ? "microsoft" : "offline";

    private static bool LooksLikePng(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[8];
            using var fs = File.OpenRead(path);
            if (fs.Read(header) < 8)
                return false;
            return header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        }
        catch
        {
            return false;
        }
    }
}
