using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

/// <summary>
/// Filter state and catalog results for the Mod download section.
/// </summary>
public partial class ModSearchViewModel : ObservableObject
{
    public const string SourceIdAll = "all";
    public const string SourceIdCurseForge = "curseforge";
    public const string SourceIdModrinth = "modrinth";

    public const string VersionIdAll = "";
    public const string LoaderIdAny = "0";

    private static readonly (string Id, string LocKey)[] SourceDefs =
    [
        (SourceIdAll, LocKeys.Mod_SourceAll),
        (SourceIdModrinth, LocKeys.Mod_SourceModrinth),
        (SourceIdCurseForge, LocKeys.Mod_SourceCurseForge)
    ];

    private static readonly (string Id, string LocKey)[] LoaderDefs =
    [
        (LoaderIdAny, LocKeys.Mod_LoaderAny),
        ("1", LocKeys.Mod_LoaderForge),
        ("16", LocKeys.Mod_LoaderNeoForge),
        ("4", LocKeys.Mod_LoaderFabric),
        ("8", LocKeys.Mod_LoaderQuilt),
        ("2", LocKeys.Mod_LoaderLiteLoader)
    ];

    /// <summary>Stable category id (platform-specific tokens) paired with a display Loc key.</summary>
    private static readonly (string Id, string LocKey)[] CategoryDefs =
    [
        ("", LocKeys.Mod_CategoryAll),
        ("406/worldgen", LocKeys.Mod_CategoryWorldGen),
        ("407/", LocKeys.Mod_CategoryBiomes),
        ("410/", LocKeys.Mod_CategoryDimensions),
        ("408/", LocKeys.Mod_CategoryOres),
        ("409/", LocKeys.Mod_CategoryStructures),
        ("412/technology", LocKeys.Mod_CategoryTechnology),
        ("415/", LocKeys.Mod_CategoryLogistics),
        ("4843/", LocKeys.Mod_CategoryAutomation),
        ("417/", LocKeys.Mod_CategoryEnergy),
        ("4558/", LocKeys.Mod_CategoryRedstone),
        ("436/food", LocKeys.Mod_CategoryFood),
        ("416/", LocKeys.Mod_CategoryFarming),
        ("/game-mechanics", LocKeys.Mod_CategoryGameMechanics),
        ("414/transportation", LocKeys.Mod_CategoryTransport),
        ("420/storage", LocKeys.Mod_CategoryStorage),
        ("419/magic", LocKeys.Mod_CategoryMagic),
        ("422/adventure", LocKeys.Mod_CategoryAdventure),
        ("424/decoration", LocKeys.Mod_CategoryDecoration),
        ("411/mobs", LocKeys.Mod_CategoryMobs),
        ("5191/utility", LocKeys.Mod_CategoryUtility),
        ("434/equipment", LocKeys.Mod_CategoryEquipment),
        ("9026/", LocKeys.Mod_CategoryCreative),
        ("6814/optimization", LocKeys.Mod_CategoryOptimization),
        ("423/", LocKeys.Mod_CategoryInfo),
        ("435/social", LocKeys.Mod_CategorySocial),
        ("421/library", LocKeys.Mod_CategoryLibrary)
    ];

    private static readonly string[] VersionPresetIds =
    [
        "26.1", "1.21.11", "1.21.8", "1.21.4", "1.21.1", "1.20.1", "1.19.2", "1.18.2", "1.16.5", "1.12.2", "1.7.10"
    ];

    private readonly DispatcherQueue _dispatcher;
    private readonly ModCatalogService _catalog = new();
    private CancellationTokenSource? _searchCts;
    private string? _lastWarning;
    private bool _syncingVersion;
    private int _currentPage = 1;

    public ModSearchViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        SourceOptions = SourceDefs.Select(d => new NamedOption { Id = d.Id, Name = Loc.Get(d.LocKey) }).ToList();
        LoaderOptions = LoaderDefs.Select(d => new NamedOption { Id = d.Id, Name = Loc.Get(d.LocKey) }).ToList();
        CategoryOptions = CategoryDefs.Select(d => new NamedOption { Id = d.Id, Name = Loc.Get(d.LocKey) }).ToList();
        VersionOptions = BuildVersionOptions();

        _selectedSource = SourceOptions[0];
        _selectedLoader = LoaderOptions[0];
        _selectedCategory = CategoryOptions[0];
        _selectedVersion = VersionOptions[0];
        _versionText = VersionOptions[0].Name;
        _statusText = Loc.Get(LocKeys.Mod_SearchHint);
    }

    public IReadOnlyList<NamedOption> SourceOptions { get; }
    public IReadOnlyList<NamedOption> LoaderOptions { get; }
    public IReadOnlyList<NamedOption> CategoryOptions { get; }
    public IReadOnlyList<NamedOption> VersionOptions { get; private set; }

    [ObservableProperty] private IReadOnlyList<ModProjectItem> _results = Array.Empty<ModProjectItem>();
    [ObservableProperty] private string _keyword = string.Empty;
    [ObservableProperty] private NamedOption? _selectedSource;
    [ObservableProperty] private NamedOption? _selectedCategory;
    [ObservableProperty] private NamedOption? _selectedLoader;
    [ObservableProperty] private NamedOption? _selectedVersion;
    [ObservableProperty] private string _versionText = string.Empty;
    [ObservableProperty] private bool _isLoaderFilterVisible;
    [ObservableProperty] private ModSearchCriteria? _submittedCriteria;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _canGoPrevious;
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _isPagerVisible;
    [ObservableProperty] private string _pageLabel = string.Empty;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _isLiteLoaderUnsupported;
    [ObservableProperty] private string _liteLoaderHintText = string.Empty;

    public void Relocalize()
    {
        ApplyNames(SourceOptions, SourceDefs);
        ApplyNames(LoaderOptions, LoaderDefs);
        ApplyNames(CategoryOptions, CategoryDefs);

        var previousVersionId = SelectedVersion?.Id ?? VersionIdAll;
        VersionOptions = BuildVersionOptions();
        OnPropertyChanged(nameof(VersionOptions));
        SelectedVersion = VersionOptions.FirstOrDefault(v => v.Id == previousVersionId) ?? VersionOptions[0];
        if (string.IsNullOrWhiteSpace(VersionText) || VersionOptions.Any(v => v.Name == VersionText))
            VersionText = SelectedVersion.Name;

        UpdateLoaderVisibility();
        UpdateLiteLoaderAvailability();

        if (!IsSearching && Results.Count == 0 && SubmittedCriteria is null)
            StatusText = Loc.Get(LocKeys.Mod_SearchHint);

        if (IsPagerVisible)
            PageLabel = Loc.Format(LocKeys.Mod_PageLabel, _currentPage);
    }

    [RelayCommand]
    private Task SearchAsync() => FetchPageAsync(page: 1, replaceCriteria: true);

    [RelayCommand]
    private Task PreviousPageAsync() => FetchPageAsync(_currentPage - 1, replaceCriteria: false);

    [RelayCommand]
    private Task NextPageAsync() => FetchPageAsync(_currentPage + 1, replaceCriteria: false);

    private async Task FetchPageAsync(int page, bool replaceCriteria)
    {
        if (page < 1 || IsSearching)
            return;

        ModSearchCriteria criteria;
        if (replaceCriteria)
        {
            criteria = CaptureCriteria();
            SubmittedCriteria = criteria;
        }
        else
        {
            if (SubmittedCriteria is null)
                return;
            if (page < _currentPage && !CanGoPrevious)
                return;
            if (page > _currentPage && !CanGoNext)
                return;
            criteria = SubmittedCriteria;
        }

        if (replaceCriteria &&
            criteria.LoaderId == ModLoaderCompatibility.LiteLoaderId &&
            !ModLoaderCompatibility.IsLiteLoaderCompatible(criteria.GameVersion))
        {
            _searchCts?.Cancel();
            Results = Array.Empty<ModProjectItem>();
            HasResults = false;
            IsPagerVisible = false;
            CanGoPrevious = false;
            CanGoNext = false;
            _currentPage = 1;
            _lastWarning = null;
            StatusText = Loc.Format(LocKeys.Mod_LiteLoaderUnsupportedStatus, criteria.GameVersion);
            IsSearching = false;
            return;
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        StatusText = Loc.Get(LocKeys.Mod_Searching);
        if (replaceCriteria)
        {
            HasResults = false;
            IsPagerVisible = false;
            _lastWarning = null;
        }

        var offset = (page - 1) * ModCatalogService.PageSize;

        try
        {
            var result = await _catalog.SearchAsync(criteria, offset, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
                return;

            RunOnUi(() =>
            {
                Results = result.Items;
                HasResults = Results.Count > 0;
                _currentPage = page;
                CanGoPrevious = page > 1;
                CanGoNext = result.HasMore;
                IsPagerVisible = HasResults || page > 1 || result.HasMore;
                PageLabel = Loc.Format(LocKeys.Mod_PageLabel, page);
                if (result.WarningMessage is not null || replaceCriteria)
                    _lastWarning = result.WarningMessage;
                StatusText = FormatStatus(Results.Count, _lastWarning);
                IsSearching = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search.
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                if (replaceCriteria)
                {
                    Results = Array.Empty<ModProjectItem>();
                    HasResults = false;
                    IsPagerVisible = false;
                    CanGoPrevious = false;
                    CanGoNext = false;
                }

                StatusText = Loc.Format(LocKeys.Mod_SearchFailed, ex.Message);
                IsSearching = false;
            });
        }
    }

    [RelayCommand]
    private void Reset()
    {
        _searchCts?.Cancel();
        Keyword = string.Empty;
        SelectedSource = SourceOptions[0];
        SelectedCategory = CategoryOptions[0];
        SelectedLoader = LoaderOptions[0];
        SelectedVersion = VersionOptions[0];
        VersionText = VersionOptions[0].Name;
        SubmittedCriteria = null;
        Results = Array.Empty<ModProjectItem>();
        HasResults = false;
        IsSearching = false;
        CanGoPrevious = false;
        CanGoNext = false;
        IsPagerVisible = false;
        PageLabel = string.Empty;
        _currentPage = 1;
        _lastWarning = null;
        StatusText = Loc.Get(LocKeys.Mod_SearchHint);
        UpdateLoaderVisibility();
        UpdateLiteLoaderAvailability();
    }

    /// <summary>
    /// Current UI filters (even before Search is pressed) for detail / install biasing.
    /// </summary>
    public ModSearchHint GetActiveHint()
    {
        var version = ResolveConcreteGameVersion();
        var loaderId = IsLoaderFilterVisible
            ? (SelectedLoader?.Id ?? LoaderIdAny)
            : LoaderIdAny;
        return new ModSearchHint(version, loaderId);
    }

    private static string FormatStatus(int count, string? warning) =>
        warning is not null
            ? Loc.Format(LocKeys.Mod_SearchCountWithWarning, count, warning)
            : count == 0
                ? Loc.Get(LocKeys.Mod_SearchEmpty)
                : Loc.Format(LocKeys.Mod_SearchCount, count);

    /// <summary>
    /// Applies version text from the editable ComboBox before search, so focus-loss
    /// cannot replace a typed custom version with the previous selection.
    /// </summary>
    public void CommitVersionInput(string? text)
    {
        VersionText = text?.Trim() ?? string.Empty;
    }

    partial void OnSelectedVersionChanged(NamedOption? value)
    {
        if (_syncingVersion)
            return;
        if (value is not null)
            VersionText = value.Name;
        UpdateLoaderVisibility();
        UpdateLiteLoaderAvailability();
    }

    partial void OnVersionTextChanged(string value)
    {
        if (!_syncingVersion)
            SyncSelectedVersionFromText(value);
        UpdateLoaderVisibility();
        UpdateLiteLoaderAvailability();
    }

    partial void OnSelectedLoaderChanged(NamedOption? value) => UpdateLiteLoaderAvailability();

    private void SyncSelectedVersionFromText(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        NamedOption? match = null;
        foreach (var opt in VersionOptions)
        {
            if (string.Equals(opt.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(opt.Id) &&
                 string.Equals(opt.Id, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                match = opt;
                break;
            }
        }

        if (ReferenceEquals(SelectedVersion, match))
            return;

        _syncingVersion = true;
        SelectedVersion = match;
        _syncingVersion = false;
    }

    private void UpdateLoaderVisibility()
    {
        var text = VersionText?.Trim() ?? string.Empty;
        var isConcrete = !string.IsNullOrEmpty(text)
            && !string.Equals(text, Loc.Get(LocKeys.Mod_VersionAll), StringComparison.Ordinal)
            && (text.Contains('.') || text.Contains('w', StringComparison.OrdinalIgnoreCase));
        IsLoaderFilterVisible = isConcrete;
        if (!isConcrete && LoaderOptions.Count > 0)
            SelectedLoader = LoaderOptions[0];
    }

    private void UpdateLiteLoaderAvailability()
    {
        var version = ResolveConcreteGameVersion();
        var compatible = ModLoaderCompatibility.IsLiteLoaderCompatible(version);
        var lite = LoaderOptions.FirstOrDefault(o => o.Id == ModLoaderCompatibility.LiteLoaderId);
        if (lite is not null)
        {
            lite.Name = compatible
                ? Loc.Get(LocKeys.Mod_LoaderLiteLoader)
                : Loc.Get(LocKeys.Mod_LoaderLiteLoaderUnsupported);
        }

        var showHint = !compatible &&
                       IsLoaderFilterVisible &&
                       SelectedLoader?.Id == ModLoaderCompatibility.LiteLoaderId;
        IsLiteLoaderUnsupported = showHint;
        LiteLoaderHintText = showHint
            ? Loc.Format(LocKeys.Mod_LiteLoaderUnsupportedHint, version)
            : string.Empty;
    }

    private string ResolveConcreteGameVersion()
    {
        var version = VersionText?.Trim() ?? string.Empty;
        if (string.Equals(version, Loc.Get(LocKeys.Mod_VersionAll), StringComparison.Ordinal))
            return string.Empty;

        if (SelectedVersion is { Id.Length: > 0 } selected &&
            (string.Equals(selected.Name, version, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(selected.Id, version, StringComparison.OrdinalIgnoreCase)))
        {
            return selected.Id;
        }

        return version;
    }

    private ModSearchCriteria CaptureCriteria()
    {
        var version = ResolveConcreteGameVersion();

        return new ModSearchCriteria(
            Keyword.Trim(),
            SelectedSource?.Id ?? SourceIdAll,
            version,
            SelectedCategory?.Id ?? string.Empty,
            IsLoaderFilterVisible ? (SelectedLoader?.Id ?? LoaderIdAny) : LoaderIdAny);
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => action());
    }

    private static List<NamedOption> BuildVersionOptions()
    {
        var list = new List<NamedOption>
        {
            new() { Id = VersionIdAll, Name = Loc.Get(LocKeys.Mod_VersionAll) }
        };
        foreach (var id in VersionPresetIds)
            list.Add(new NamedOption { Id = id, Name = id });
        return list;
    }

    private static void ApplyNames(IReadOnlyList<NamedOption> options, (string Id, string LocKey)[] defs)
    {
        for (var i = 0; i < options.Count && i < defs.Length; i++)
            options[i].Name = Loc.Get(defs[i].LocKey);
    }
}

/// <summary>Immutable snapshot of Mod filter values after the user submits a search.</summary>
public sealed record ModSearchCriteria(
    string Keyword,
    string SourceId,
    string GameVersion,
    string CategoryId,
    string LoaderId);
