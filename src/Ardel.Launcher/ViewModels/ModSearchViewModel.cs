using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
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
        (SourceIdCurseForge, LocKeys.Mod_SourceCurseForge),
        (SourceIdModrinth, LocKeys.Mod_SourceModrinth)
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
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private bool _hasResults;

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

        if (!IsSearching && Results.Count == 0 && SubmittedCriteria is null)
            StatusText = Loc.Get(LocKeys.Mod_SearchHint);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var criteria = CaptureCriteria();
        SubmittedCriteria = criteria;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        StatusText = Loc.Get(LocKeys.Mod_Searching);
        HasResults = false;

        try
        {
            var result = await _catalog.SearchAsync(criteria, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
                return;

            RunOnUi(() =>
            {
                Results = result.Items;
                HasResults = Results.Count > 0;
                StatusText = result.WarningMessage is not null
                    ? Loc.Format(LocKeys.Mod_SearchCountWithWarning, Results.Count, result.WarningMessage)
                    : Results.Count == 0
                        ? Loc.Get(LocKeys.Mod_SearchEmpty)
                        : Loc.Format(LocKeys.Mod_SearchCount, Results.Count);
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
                Results = Array.Empty<ModProjectItem>();
                HasResults = false;
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
        StatusText = Loc.Get(LocKeys.Mod_SearchHint);
        UpdateLoaderVisibility();
    }

    partial void OnSelectedVersionChanged(NamedOption? value)
    {
        if (value is not null)
            VersionText = value.Name;
        UpdateLoaderVisibility();
    }

    partial void OnVersionTextChanged(string value) => UpdateLoaderVisibility();

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

    private ModSearchCriteria CaptureCriteria()
    {
        var version = VersionText?.Trim() ?? string.Empty;
        if (string.Equals(version, Loc.Get(LocKeys.Mod_VersionAll), StringComparison.Ordinal))
            version = string.Empty;

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
