using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

/// <summary>In-page Mod project detail (opened from catalog results).</summary>
public partial class ModDetailViewModel : ObservableObject
{
    public const string FilterIdAll = "";

    private readonly DispatcherQueue _dispatcher;
    private readonly ModCatalogService _catalog = new();
    private readonly Stack<(ModProjectItem Project, ModSearchHint Hint)> _backStack = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _depsCts;
    private ModProjectDetail? _detail;
    private ModProjectItem? _currentProject;
    private IReadOnlyList<ModFileVersionItem> _allFiles = Array.Empty<ModFileVersionItem>();
    private string _hintGameVersion = string.Empty;
    private string _hintLoaderSlug = string.Empty;
    private bool _suppressFilterApply;
    private int _depsGeneration;

    public ModDetailViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ModProjectDetail? Detail => _detail;

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
    [ObservableProperty] private string _versionsLabel = string.Empty;
    [ObservableProperty] private string _loadersLabel = string.Empty;
    [ObservableProperty] private bool _hasVersions;
    [ObservableProperty] private bool _hasLoaders;
    [ObservableProperty] private Uri? _iconUri;
    [ObservableProperty] private bool _hasIcon;

    [ObservableProperty] private IReadOnlyList<NamedOption> _gameVersionFilters = Array.Empty<NamedOption>();
    [ObservableProperty] private IReadOnlyList<NamedOption> _loaderFilters = Array.Empty<NamedOption>();
    [ObservableProperty] private NamedOption? _selectedGameVersionFilter;
    [ObservableProperty] private NamedOption? _selectedLoaderFilter;
    [ObservableProperty] private IReadOnlyList<ModFileVersionItem> _visibleFiles = Array.Empty<ModFileVersionItem>();

    [ObservableProperty] private IReadOnlyList<ModDependencyItem> _dependencies = Array.Empty<ModDependencyItem>();
    [ObservableProperty] private bool _hasDependencies;
    [ObservableProperty] private bool _isLoadingDependencies;

    public async Task OpenAsync(ModProjectItem project, ModSearchHint? searchHint = null)
    {
        _backStack.Clear();
        await OpenCoreAsync(project, searchHint, pushCurrent: false).ConfigureAwait(true);
    }

    public async Task OpenDependencyAsync(ModDependencyItem dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        await OpenCoreAsync(dependency.ToProjectItem(), BuildCurrentHint(), pushCurrent: true)
            .ConfigureAwait(true);
    }

    private async Task OpenCoreAsync(ModProjectItem project, ModSearchHint? searchHint, bool pushCurrent)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (pushCurrent && _currentProject is not null)
            _backStack.Push((_currentProject, BuildCurrentHint()));

        _currentProject = project;
        ApplyHint(searchHint);

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsOpen = true;
        IsLoading = true;
        ClearDetailContent();
        ActionFeedback = string.Empty;
        StatusText = Loc.Get(LocKeys.Mod_DetailLoading);
        IsModrinthSource = project.SourceId == ModSearchViewModel.SourceIdModrinth;
        IsCurseForgeSource = project.SourceId == ModSearchViewModel.SourceIdCurseForge;

        try
        {
            var detail = await _catalog.GetProjectDetailAsync(project, token).ConfigureAwait(false);
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
            await OpenCoreAsync(previous.Project, previous.Hint, pushCurrent: false).ConfigureAwait(true);
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
        if (_detail is null)
            return;

        var package = new DataPackage();
        package.SetText(_detail.Title);
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

    private void ApplyDetail(ModProjectDetail detail)
    {
        _detail = detail;
        OnPropertyChanged(nameof(Detail));
        Title = detail.Title;
        Description = detail.Description;
        SourceLabel = detail.SourceLabel;
        VersionsLabel = detail.VersionsLabel;
        LoadersLabel = detail.LoadersLabel;
        HasVersions = detail.HasVersions;
        HasLoaders = detail.HasLoaders;
        IconUri = detail.IconUri;
        HasIcon = detail.HasIcon;
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
            .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
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
        var loaderId = SelectedLoaderFilter?.Id ?? FilterIdAll;

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
        var generation = ++_depsGeneration;
        _depsCts?.Cancel();
        _depsCts?.Dispose();
        _depsCts = new CancellationTokenSource();
        var token = _depsCts.Token;

        var file = VisibleFiles.FirstOrDefault();
        if (file is null || file.Dependencies.Count == 0)
        {
            Dependencies = Array.Empty<ModDependencyItem>();
            HasDependencies = false;
            IsLoadingDependencies = false;
            return;
        }

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
        VersionsLabel = string.Empty;
        LoadersLabel = string.Empty;
        HasVersions = false;
        HasLoaders = false;
        IconUri = null;
        HasIcon = false;
        _allFiles = Array.Empty<ModFileVersionItem>();
        VisibleFiles = Array.Empty<ModFileVersionItem>();
        GameVersionFilters = Array.Empty<NamedOption>();
        LoaderFilters = Array.Empty<NamedOption>();
        SelectedGameVersionFilter = null;
        SelectedLoaderFilter = null;
        Dependencies = Array.Empty<ModDependencyItem>();
        HasDependencies = false;
        IsLoadingDependencies = false;
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => action());
    }
}

/// <summary>Active Mod search filters used to bias detail + install matching.</summary>
public sealed record ModSearchHint(string GameVersion, string LoaderId);
