using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

/// <summary>In-page catalog project detail (opened from search results).</summary>
public partial class ModDetailViewModel : ObservableObject
{
    public const string FilterIdAll = "";

    private readonly DispatcherQueue _dispatcher;
    private readonly ModCatalogService _catalog = new();
    private readonly Stack<(ModProjectItem Project, ModSearchHint Hint, CatalogProjectKind Kind)> _backStack = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _depsCts;
    private ModProjectDetail? _detail;
    private ModProjectItem? _currentProject;
    private IReadOnlyList<ModFileVersionItem> _allFiles = Array.Empty<ModFileVersionItem>();
    private string _hintGameVersion = string.Empty;
    private string _hintLoaderSlug = string.Empty;
    private bool _suppressFilterApply;
    private int _depsGeneration;
    private string? _resolvedDepsFileId;
    private CatalogProjectKind _kind = CatalogProjectKind.Mod;

    public ModDetailViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ModProjectDetail? Detail => _detail;
    public CatalogProjectKind ProjectKind => _kind;

    /// <summary>Search-page version / loader carried into install matching.</summary>
    public string HintGameVersion => _hintGameVersion;
    public string HintLoaderSlug => _hintLoaderSlug;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isModrinthSource;
    [ObservableProperty] private bool _isCurseForgeSource;
    [ObservableProperty] private string _actionFeedback = string.Empty;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _sourceLabel = string.Empty;
    [ObservableProperty] private Uri? _iconUri;
    [ObservableProperty] private bool _hasIcon;
    [ObservableProperty] private ImageSource? _iconImage;
    [ObservableProperty] private bool _showLoaderFilter = true;
    [ObservableProperty] private bool _showDependencies = true;

    [ObservableProperty] private IReadOnlyList<NamedOption> _gameVersionFilters = Array.Empty<NamedOption>();
    [ObservableProperty] private IReadOnlyList<NamedOption> _loaderFilters = Array.Empty<NamedOption>();
    [ObservableProperty] private NamedOption? _selectedGameVersionFilter;
    [ObservableProperty] private NamedOption? _selectedLoaderFilter;
    [ObservableProperty] private IReadOnlyList<ModFileVersionItem> _visibleFiles = Array.Empty<ModFileVersionItem>();

    [ObservableProperty] private IReadOnlyList<ModDependencyItem> _dependencies = Array.Empty<ModDependencyItem>();
    [ObservableProperty] private bool _hasDependencies;
    [ObservableProperty] private bool _isLoadingDependencies;

    public async Task OpenAsync(
        ModProjectItem project,
        ModSearchHint? searchHint = null,
        CatalogProjectKind kind = CatalogProjectKind.Mod)
    {
        _backStack.Clear();
        await OpenCoreAsync(project, searchHint, kind, pushCurrent: false).ConfigureAwait(true);
    }

    public async Task OpenDependencyAsync(ModDependencyItem dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        await OpenCoreAsync(dependency.ToProjectItem(), BuildCurrentHint(), CatalogProjectKind.Mod, pushCurrent: true)
            .ConfigureAwait(true);
    }

    private async Task OpenCoreAsync(
        ModProjectItem project,
        ModSearchHint? searchHint,
        CatalogProjectKind kind,
        bool pushCurrent)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (pushCurrent && _currentProject is not null)
            _backStack.Push((_currentProject, BuildCurrentHint(), _kind));

        _currentProject = project;
        _kind = kind;
        ApplyHint(searchHint);
        ShowLoaderFilter = kind == CatalogProjectKind.Mod;
        ShowDependencies = kind == CatalogProjectKind.Mod;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsOpen = true;
        IsLoading = true;
        _resolvedDepsFileId = null;
        ActionFeedback = string.Empty;
        StatusText = Loc.Get(LocKeys.Mod_DetailLoading);
        IsModrinthSource = project.SourceId == ModSearchViewModel.SourceIdModrinth;
        IsCurseForgeSource = project.SourceId == ModSearchViewModel.SourceIdCurseForge;

        // Paint list-row metadata immediately so the page does not feel blank.
        ApplyShell(project);

        try
        {
            var detail = await _catalog
                .GetProjectDetailAsync(
                    project,
                    token,
                    _hintGameVersion,
                    _hintLoaderSlug,
                    kind)
                .ConfigureAwait(false);
            if (token.IsCancellationRequested)
                return;

            RunOnUi(() => ApplyDetail(detail));
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                StatusText = Loc.Format(LocKeys.Mod_DetailLoadFailedNamed, ex.Message);
                IsLoading = false;
            });
        }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        _loadCts?.Cancel();
        _depsCts?.Cancel();

        if (_backStack.Count > 0)
        {
            var previous = _backStack.Pop();
            await OpenCoreAsync(previous.Project, previous.Hint, previous.Kind, pushCurrent: false)
                .ConfigureAwait(true);
            return;
        }

        IsOpen = false;
        _currentProject = null;
        ClearDetailContent();
        IsLoading = false;
        ActionFeedback = string.Empty;
        StatusText = string.Empty;
        _hintGameVersion = string.Empty;
        _hintLoaderSlug = string.Empty;
    }

    [RelayCommand]
    private async Task OpenSourceAsync()
    {
        if (_detail is null || string.IsNullOrWhiteSpace(_detail.ProjectUrl))
            return;

        if (!Uri.TryCreate(_detail.ProjectUrl, UriKind.Absolute, out var uri))
            return;

        await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand]
    private void CopyName()
    {
        if (_detail is null && string.IsNullOrWhiteSpace(Title))
            return;

        var package = new DataPackage();
        package.SetText(_detail?.Title ?? Title);
        Clipboard.SetContent(package);
        ActionFeedback = Loc.Get(LocKeys.Mod_DetailCopiedName);
    }

    partial void OnSelectedGameVersionFilterChanged(NamedOption? value) => ApplyFileFilter();

    partial void OnSelectedLoaderFilterChanged(NamedOption? value) => ApplyFileFilter();

    private void ApplyHint(ModSearchHint? searchHint)
    {
        var rawVersion = searchHint?.GameVersion?.Trim() ?? string.Empty;
        var rawLoader = searchHint?.LoaderId?.Trim() ?? string.Empty;
        _hintGameVersion = rawVersion;
        _hintLoaderSlug = ModInstanceMatcher.LoaderIdToSlug(rawLoader)
                          ?? (IsKnownLoaderSlug(rawLoader) ? rawLoader.ToLowerInvariant() : string.Empty);
    }

    private ModSearchHint BuildCurrentHint()
    {
        var version = SelectedGameVersionFilter is { Id.Length: > 0 } vf
            ? vf.Id
            : _hintGameVersion;
        var loader = SelectedLoaderFilter is { Id.Length: > 0 } lf
            ? lf.Id
            : _hintLoaderSlug;
        return new ModSearchHint(version, loader);
    }

    private static bool IsKnownLoaderSlug(string value) =>
        value is "forge" or "neoforge" or "fabric" or "quilt" or "liteloader";

    private void ApplyShell(ModProjectItem project)
    {
        Title = project.Title;
        Description = project.Description;
        SourceLabel = project.SourceLabel;
        IconUri = project.IconUri;
        HasIcon = project.HasIcon;
        IconImage = CatalogIconCache.Get(project.IconUri, decodePixels: 96);
        HasDetail = true;
        VisibleFiles = Array.Empty<ModFileVersionItem>();
        Dependencies = Array.Empty<ModDependencyItem>();
        HasDependencies = false;
        IsLoadingDependencies = false;
    }

    private void ApplyDetail(ModProjectDetail detail)
    {
        _detail = detail;
        OnPropertyChanged(nameof(Detail));
        Title = detail.Title;
        Description = detail.Description;
        SourceLabel = detail.SourceLabel;
        IconUri = detail.IconUri;
        HasIcon = detail.HasIcon;
        IconImage = CatalogIconCache.Get(detail.IconUri, decodePixels: 96);
        _allFiles = detail.Files;
        HasDetail = true;
        IsModrinthSource = detail.SourceId == ModSearchViewModel.SourceIdModrinth;
        IsCurseForgeSource = detail.SourceId == ModSearchViewModel.SourceIdCurseForge;

        BuildFilters(detail.Files);
        ApplyFileFilter();
        IsLoading = false;
    }

    private void BuildFilters(IReadOnlyList<ModFileVersionItem> files)
    {
        var versions = files
            .SelectMany(f => f.GameVersions)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(v => v, MinecraftVersionOrder.Ascending)
            .ToList();

        var loaders = files
            .SelectMany(f => f.Loaders)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var versionOptions = new List<NamedOption>
        {
            new() { Id = FilterIdAll, Name = Loc.Get(LocKeys.Mod_DetailFilterAllVersions) }
        };
        foreach (var v in versions)
            versionOptions.Add(new NamedOption { Id = v, Name = v });

        var loaderOptions = new List<NamedOption>
        {
            new() { Id = FilterIdAll, Name = Loc.Get(LocKeys.Mod_DetailFilterAllLoaders) }
        };
        foreach (var slug in loaders)
        {
            loaderOptions.Add(new NamedOption
            {
                Id = slug,
                Name = FormatLoaderName(slug)
            });
        }

        _suppressFilterApply = true;
        GameVersionFilters = versionOptions;
        LoaderFilters = loaderOptions;

        SelectedGameVersionFilter =
            versionOptions.FirstOrDefault(o =>
                o.Id.Length > 0 &&
                string.Equals(o.Id, _hintGameVersion, StringComparison.OrdinalIgnoreCase))
            ?? versionOptions[0];

        SelectedLoaderFilter =
            loaderOptions.FirstOrDefault(o =>
                o.Id.Length > 0 &&
                string.Equals(o.Id, _hintLoaderSlug, StringComparison.OrdinalIgnoreCase))
            ?? loaderOptions[0];
        _suppressFilterApply = false;
    }

    private void ApplyFileFilter()
    {
        if (_suppressFilterApply)
            return;

        var versionId = SelectedGameVersionFilter?.Id ?? FilterIdAll;
        var loaderId = ShowLoaderFilter
            ? (SelectedLoaderFilter?.Id ?? FilterIdAll)
            : FilterIdAll;

        IEnumerable<ModFileVersionItem> query = _allFiles;
        if (!string.IsNullOrEmpty(versionId))
        {
            query = query.Where(f =>
                f.GameVersions.Any(v =>
                    string.Equals(v, versionId, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrEmpty(loaderId))
        {
            query = query.Where(f =>
                f.Loaders.Any(l =>
                    string.Equals(l, loaderId, StringComparison.OrdinalIgnoreCase)));
        }

        VisibleFiles = query
            .OrderByDescending(f => FileRank(f, versionId, loaderId))
            .ThenByDescending(f => f.Channel == ModReleaseChannel.Release)
            .ThenByDescending(f => f.Published ?? DateTimeOffset.MinValue)
            .ToList();

        StatusText = VisibleFiles.Count == 0
            ? Loc.Get(LocKeys.Mod_DetailNoFiles)
            : Loc.Format(LocKeys.Mod_DetailFileCount, VisibleFiles.Count);

        _ = RefreshDependenciesAsync();
    }

    private async Task RefreshDependenciesAsync()
    {
        if (!ShowDependencies)
        {
            Dependencies = Array.Empty<ModDependencyItem>();
            HasDependencies = false;
            IsLoadingDependencies = false;
            return;
        }

        var file = VisibleFiles.FirstOrDefault();
        if (file is null || file.Dependencies.Count == 0)
        {
            _resolvedDepsFileId = file?.Id;
            Dependencies = Array.Empty<ModDependencyItem>();
            HasDependencies = false;
            IsLoadingDependencies = false;
            return;
        }

        if (string.Equals(_resolvedDepsFileId, file.Id, StringComparison.Ordinal) &&
            HasDependencies)
            return;

        var generation = ++_depsGeneration;
        _depsCts?.Cancel();
        _depsCts?.Dispose();
        _depsCts = new CancellationTokenSource();
        var token = _depsCts.Token;

        IsLoadingDependencies = true;
        var versionId = SelectedGameVersionFilter is { Id.Length: > 0 } vf ? vf.Id : _hintGameVersion;
        var loaderId = SelectedLoaderFilter is { Id.Length: > 0 } lf ? lf.Id : _hintLoaderSlug;

        try
        {
            var deps = await _catalog
                .ResolveDependenciesAsync(file.Dependencies, versionId, loaderId, token)
                .ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _depsGeneration)
                return;

            RunOnUi(() =>
            {
                _resolvedDepsFileId = file.Id;
                Dependencies = deps;
                HasDependencies = deps.Count > 0;
                IsLoadingDependencies = false;
            });
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
        catch
        {
            if (generation != _depsGeneration)
                return;
            RunOnUi(() =>
            {
                Dependencies = Array.Empty<ModDependencyItem>();
                HasDependencies = false;
                IsLoadingDependencies = false;
            });
        }
    }

    private static int FileRank(ModFileVersionItem file, string versionFilter, string loaderFilter)
    {
        var score = 0;
        if (!string.IsNullOrEmpty(versionFilter) &&
            file.GameVersions.Any(v => string.Equals(v, versionFilter, StringComparison.OrdinalIgnoreCase)))
            score += 2;
        if (!string.IsNullOrEmpty(loaderFilter) &&
            file.Loaders.Any(l => string.Equals(l, loaderFilter, StringComparison.OrdinalIgnoreCase)))
            score += 2;
        return score;
    }

    private static string FormatLoaderName(string slug) => slug.ToLowerInvariant() switch
    {
        "forge" => Loc.Get(LocKeys.Mod_LoaderForge),
        "neoforge" => Loc.Get(LocKeys.Mod_LoaderNeoForge),
        "fabric" => Loc.Get(LocKeys.Mod_LoaderFabric),
        "quilt" => Loc.Get(LocKeys.Mod_LoaderQuilt),
        "liteloader" => Loc.Get(LocKeys.Mod_LoaderLiteLoader),
        _ => slug
    };

    private void ClearDetailContent()
    {
        _detail = null;
        OnPropertyChanged(nameof(Detail));
        HasDetail = false;
        Title = string.Empty;
        Description = string.Empty;
        SourceLabel = string.Empty;
        IconUri = null;
        HasIcon = false;
        IconImage = null;
        _allFiles = Array.Empty<ModFileVersionItem>();
        VisibleFiles = Array.Empty<ModFileVersionItem>();
        GameVersionFilters = Array.Empty<NamedOption>();
        LoaderFilters = Array.Empty<NamedOption>();
        SelectedGameVersionFilter = null;
        SelectedLoaderFilter = null;
        Dependencies = Array.Empty<ModDependencyItem>();
        HasDependencies = false;
        IsLoadingDependencies = false;
        _resolvedDepsFileId = null;
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => action());
    }
}

/// <summary>Active catalog search filters used to bias detail + install matching.</summary>
public sealed record ModSearchHint(string GameVersion, string LoaderId);
