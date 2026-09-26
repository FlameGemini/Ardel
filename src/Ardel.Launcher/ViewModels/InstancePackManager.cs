using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

/// <summary>
/// Instance-folder pack list (resource / data / shader): install, delete, catalog match, updates.
/// Packs are not enable/disable toggled — Minecraft loads whatever is in the folder.
/// </summary>
public partial class InstancePackManager : ObservableObject
{
    private readonly Func<string> _getInstanceDirectory;
    private readonly Func<string> _getVersionId;
    private readonly Action _reloadLists;
    private readonly UiCoalesce _selectionCoalesce;
    private readonly List<ResourceItem> _all = new();

    public InstancePackManager(
        string folderName,
        CatalogProjectKind kind,
        DownloadSection downloadSection,
        Func<string> getInstanceDirectory,
        Func<string> getVersionId,
        Action reloadLists,
        DispatcherQueue dispatcher)
    {
        FolderName = folderName;
        Kind = kind;
        DownloadSection = downloadSection;
        FilterGroupName = "PackFilter_" + folderName;
        FilterUpdatableGroupName = FilterGroupName + "_Updatable";
        _getInstanceDirectory = getInstanceDirectory;
        _getVersionId = getVersionId;
        _reloadLists = reloadLists;
        _selectionCoalesce = new UiCoalesce(dispatcher, RefreshSelectionStateCore);
        Items = new ObservableCollection<ResourceItem>();
        Updatable = new ObservableCollection<ModUpdateCandidate>();
        RefreshUpdatableFilterLabel();
    }

    public string FolderName { get; }
    public CatalogProjectKind Kind { get; }
    public DownloadSection DownloadSection { get; }
    public string FilterGroupName { get; }
    public string FilterUpdatableGroupName { get; }

    public IReadOnlyList<ResourceItem> AllItems => _all;
    public ObservableCollection<ResourceItem> Items { get; }
    public ObservableCollection<ModUpdateCandidate> Updatable { get; }

    [ObservableProperty] private int _filterIndex;
    [ObservableProperty] private bool _hasUpdatable;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private string _updateStatusText = string.Empty;
    [ObservableProperty] private string _updatableFilterLabel = string.Empty;

    public bool HasUpdateStatus => !string.IsNullOrWhiteSpace(UpdateStatusText);
    public bool ShowRegularList => FilterIndex != 1;
    public bool ShowUpdatableList => FilterIndex == 1;
    public bool ShowFilterBar => HasUpdatable;
    public bool CanUpdateSelected => !IsUpdating && Updatable.Any(m => m.IsSelected);
    public bool HasSelected => Items.Any(m => m.IsSelected);
    public bool ShowBrowseActions => true;
    public bool ShowSelectionActions => HasSelected;
    public bool IsListEmpty => FilterIndex != 1 && Items.Count == 0;
    public string SelectedCountText =>
        Loc.Format(LocKeys.InstanceSettings_SelectedCount, Items.Count(m => m.IsSelected));
    public string EmptyMessage => Kind switch
    {
        CatalogProjectKind.ResourcePack => Loc.Get(LocKeys.InstanceSettings_EmptyResourcePacks),
        CatalogProjectKind.Datapack => Loc.Get(LocKeys.InstanceSettings_EmptyDatapacks),
        CatalogProjectKind.ShaderPack => Loc.Get(LocKeys.InstanceSettings_EmptyShaderPacks),
        _ => Loc.Get(LocKeys.InstanceSettings_EmptyMods)
    };
    private string _updateResultText = string.Empty;

    partial void OnUpdateStatusTextChanged(string value) =>
        OnPropertyChanged(nameof(HasUpdateStatus));

    partial void OnFilterIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowRegularList));
        OnPropertyChanged(nameof(ShowUpdatableList));
        if (value != 1)
            ApplyFilter();
        else
            ClearSelection();
        NotifyUpdateCanExecute();
    }

    partial void OnHasUpdatableChanged(bool value) =>
        OnPropertyChanged(nameof(ShowFilterBar));

    public void ReplaceAll(IEnumerable<ResourceItem> items)
    {
        _all.Clear();
        _all.AddRange(items);
        if (FilterIndex == 1)
            FilterIndex = 0;
        else
            ApplyFilter();

        Updatable.Clear();
        HasUpdatable = false;
        RefreshUpdatableFilterLabel();
        NotifyUpdateCanExecute();
        OnPropertyChanged(nameof(IsListEmpty));
        if (!string.IsNullOrEmpty(_updateResultText))
            UpdateStatusText = _updateResultText;
    }

    public void SetUpdatable(IReadOnlyList<ModUpdateCandidate> candidates)
    {
        Updatable.Clear();
        foreach (var candidate in candidates)
        {
            candidate.SelectionChanged = NotifyUpdateCanExecute;
            Updatable.Add(candidate);
        }

        HasUpdatable = Updatable.Count > 0;
        RefreshUpdatableFilterLabel();
        NotifyUpdateCanExecute();
        var scan = HasUpdatable
            ? Loc.Format(LocKeys.InstanceSettings_PackUpdateAvailable, Updatable.Count)
            : string.Empty;
        UpdateStatusText = !string.IsNullOrEmpty(_updateResultText)
            ? (string.IsNullOrEmpty(scan) ? _updateResultText : _updateResultText + " · " + scan)
            : scan;
    }

    public void RefreshSelectionState() => _selectionCoalesce.Schedule();

    private void ApplyFilter()
    {
        foreach (var item in _all)
        {
            item.IsSelected = false;
            item.SelectionChanged = RefreshSelectionState;
        }

        Items.Clear();
        foreach (var item in _all)
            Items.Add(item);

        RefreshSelectionStateCore();
    }

    private void RefreshSelectionStateCore()
    {
        OnPropertyChanged(nameof(HasSelected));
        OnPropertyChanged(nameof(ShowSelectionActions));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(IsListEmpty));
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ClearPackSelectionCommand.NotifyCanExecuteChanged();
    }

    public void ClearSelection()
    {
        foreach (var item in Items)
            item.IsSelected = false;
        RefreshSelectionStateCore();
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void ClearPackSelection() => ClearSelection();

    private void RefreshUpdatableFilterLabel()
    {
        UpdatableFilterLabel = HasUpdatable
            ? Loc.Format(LocKeys.InstanceSettings_ModFilterUpdatableCount, Updatable.Count)
            : Loc.Get(LocKeys.InstanceSettings_ModFilterUpdatable);
    }

    private void NotifyUpdateCanExecute()
    {
        OnPropertyChanged(nameof(CanUpdateSelected));
        UpdateSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private async Task DeleteSelectedAsync()
    {
        var targets = Items.Where(m => m.IsSelected).ToList();
        if (targets.Count == 0)
            return;

        if (!await ConfirmDialog.ConfirmDeleteAsync(targets.Select(t => t.DisplayName).ToList())
                .ConfigureAwait(true))
            return;

        foreach (var item in targets)
        {
            try
            {
                if (File.Exists(item.FullPath) || Directory.Exists(item.FullPath))
                    RecycleBinHelper.MoveToRecycleBin(item.FullPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstancePackManager] Delete {item.FileName}: {ex.Message}");
            }
        }

        ClearSelection();
        _reloadLists();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            var targetDir = Path.Combine(_getInstanceDirectory(), FolderName);
            Directory.CreateDirectory(targetDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = targetDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancePackManager] OpenFolder failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RefreshLists() => _reloadLists();

    [RelayCommand]
    private void SearchCatalog()
    {
        if (App.MainWindowInstance is not MainWindow mainWindow)
            return;

        try
        {
            var downloadVm = App.Services.GetRequiredService<DownloadViewModel>();
            var baseMcVersion = VersionKindDetector.DetectBaseGameVersion(_getVersionId());
            downloadVm.BeginInstanceModSearch(baseMcVersion, ModSearchViewModel.LoaderIdAny);
            mainWindow.NavigateToDownload();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancePackManager] SearchCatalog error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SelectAllUpdatable()
    {
        foreach (var item in Updatable)
            item.IsSelected = true;
        NotifyUpdateCanExecute();
    }

    [RelayCommand]
    private void ClearUpdatableSelection()
    {
        foreach (var item in Updatable)
            item.IsSelected = false;
        NotifyUpdateCanExecute();
    }

    [RelayCommand(CanExecute = nameof(CanUpdateSelected))]
    private async Task UpdateSelectedAsync()
    {
        var selected = Updatable.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0 || IsUpdating)
            return;

        IsUpdating = true;
        NotifyUpdateCanExecute();
        var catalog = new ModCatalogService();
        var destDir = Path.Combine(_getInstanceDirectory(), FolderName);
        Directory.CreateDirectory(destDir);

        var ok = 0;
        var fail = 0;
        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var candidate = selected[i];
                UpdateStatusText = Loc.Format(
                    LocKeys.InstanceSettings_ModUpdateProgress,
                    i + 1,
                    selected.Count,
                    candidate.DisplayName);

                try
                {
                    var destPath = Path.Combine(destDir, candidate.NewFileName);
                    await catalog.DownloadFileAsync(
                            candidate.DownloadUrl,
                            destPath,
                            progress: null,
                            CancellationToken.None)
                        .ConfigureAwait(true);

                    var oldPath = candidate.LocalMod.FullPath;
                    if ((File.Exists(oldPath) || Directory.Exists(oldPath)) &&
                        !string.Equals(oldPath, destPath, StringComparison.OrdinalIgnoreCase))
                    {
                        RecycleBinHelper.MoveToRecycleBin(oldPath);
                    }

                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    Debug.WriteLine($"[InstancePackManager] Update failed {candidate.DisplayName}: {ex.Message}");
                }
            }

            UpdateStatusText = fail == 0
                ? Loc.Format(LocKeys.InstanceSettings_PackUpdateDone, ok)
                : Loc.Format(LocKeys.InstanceSettings_PackUpdateDoneWithErrors, ok, fail);
            _updateResultText = UpdateStatusText;
        }
        finally
        {
            IsUpdating = false;
            NotifyUpdateCanExecute();
            _reloadLists();
        }
    }

    public static List<ResourceItem> Scan(string folderPath, CatalogProjectKind kind)
    {
        var list = new List<ResourceItem>();
        if (!Directory.Exists(folderPath))
            return list;

        try
        {
            var files = Directory.GetFiles(folderPath, "*.*")
                .Where(IsZipPack)
                .Select(p => new FileInfo(p));

            foreach (var file in files)
            {
                list.Add(new ResourceItem(
                    file.Name,
                    FormatSize(file.Length),
                    file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                    file.FullName,
                    isToggleable: false,
                    projectKind: kind));
            }

            var dirs = Directory.GetDirectories(folderPath)
                .Select(p => new DirectoryInfo(p))
                .Where(d => !d.Name.StartsWith('.') &&
                            !d.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) &&
                            !d.Name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase));

            foreach (var dir in dirs)
            {
                list.Add(new ResourceItem(
                    dir.Name,
                    Loc.Get(LocKeys.InstanceSettings_PackFolder),
                    dir.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                    dir.FullName,
                    isToggleable: false,
                    projectKind: kind));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancePackManager] Scan {folderPath}: {ex.Message}");
        }

        list.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static bool IsZipPack(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    public static (string? Title, string? IconPath) TryExtractMeta(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return ExtractFolderMeta(path);
            if (File.Exists(path))
                return ExtractZipMeta(path);
        }
        catch
        {
            // Ignore unreadable packs.
        }

        return (null, null);
    }

    private static (string? Title, string? IconPath) ExtractFolderMeta(string folderPath)
    {
        string? title = null;
        var mcmeta = Path.Combine(folderPath, "pack.mcmeta");
        if (File.Exists(mcmeta))
        {
            try
            {
                using var stream = File.OpenRead(mcmeta);
                title = ReadPackDescription(stream);
            }
            catch
            {
                // ignore
            }
        }

        var png = Path.Combine(folderPath, "pack.png");
        return (title, File.Exists(png) ? png : null);
    }

    private static (string? Title, string? IconPath) ExtractZipMeta(string zipPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
        string? title = null;
        var metaEntry = archive.GetEntry("pack.mcmeta");
        if (metaEntry is not null)
        {
            using var stream = metaEntry.Open();
            title = ReadPackDescription(stream);
        }

        var iconEntry = archive.GetEntry("pack.png");
        if (iconEntry is null)
            return (title, null);

        try
        {
            var cacheDir = Path.Combine(GamePaths.GetLauncherDirectory(), "cache", "pack_icons");
            Directory.CreateDirectory(cacheDir);
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(zipPath)))[..12];
            var localIconPath = Path.Combine(cacheDir, hash + ".png");
            if (!File.Exists(localIconPath))
            {
                using var inStream = iconEntry.Open();
                using var outStream = File.Create(localIconPath);
                inStream.CopyTo(outStream);
            }

            return (title, localIconPath);
        }
        catch
        {
            return (title, null);
        }
    }

    private static string? ReadPackDescription(Stream stream)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("pack", out var pack) ||
            !pack.TryGetProperty("description", out var desc))
            return null;

        var raw = desc.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => desc.GetString(),
            System.Text.Json.JsonValueKind.Object when desc.TryGetProperty("text", out var text) => text.GetString(),
            System.Text.Json.JsonValueKind.Array when desc.GetArrayLength() > 0 =>
                desc[0].ValueKind == System.Text.Json.JsonValueKind.String
                    ? desc[0].GetString()
                    : desc[0].TryGetProperty("text", out var firstText) ? firstText.GetString() : null,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return System.Text.RegularExpressions.Regex.Replace(raw, "§.", string.Empty).Trim();
    }

    private static string FormatSize(long length) =>
        length > 1024 * 1024
            ? $"{length / (1024.0 * 1024.0):F1} MB"
            : $"{length / 1024.0:F1} KB";
}
