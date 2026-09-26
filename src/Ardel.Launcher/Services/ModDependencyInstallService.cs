using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Services;

public sealed class ModDependencyInstallService
{
    private const int MaxTransitiveDependencies = 999;

    private readonly ModCatalogService _catalog = new();

    public static bool HasDependencyCandidates(ModFileVersionItem file) =>
        file.Dependencies.Any(d =>
            d.Kind is ModDependencyKind.Required or ModDependencyKind.Optional &&
            !string.IsNullOrWhiteSpace(d.ProjectId));

    public async Task<ModDependencyCheckResult> GetMissingDependenciesAsync(
        GameVersionItem instance,
        ModFileVersionItem file,
        string sourceId,
        string minecraftRoot,
        CancellationToken cancellationToken = default)
    {
        var seedDeps = FilterDependencyCandidates(file.Dependencies);
        if (seedDeps.Count == 0)
            return EmptyResult();

        var instanceDir = GamePaths.EnsureVersionIsolation(instance.Id, minecraftRoot);
        var modsDir = Path.Combine(instanceDir, "mods");
        var localKeys = await Task.Run(
            () => BuildLocalInstalledKeys(modsDir),
            cancellationToken).ConfigureAwait(false);

        var mcVersion = VersionKindDetector.DetectBaseGameVersion(instance.Id, minecraftRoot);
        var loaderSlug = ModInstanceMatcher.LoaderSlugForInstance(instance);
        var projectCache = new Dictionary<string, ModProjectItem?>(StringComparer.OrdinalIgnoreCase);

        var (missingRefs, dependsOn) = await CollectMissingTransitiveAsync(
                seedDeps,
                localKeys,
                projectCache,
                mcVersion,
                loaderSlug,
                cancellationToken)
            .ConfigureAwait(false);

        if (missingRefs.Count == 0)
            return EmptyResult();

        var orderedRefs = SortDependenciesFirst(missingRefs, dependsOn);

        IReadOnlyList<ModDependencyItem> resolved;
        try
        {
            resolved = await _catalog.ResolveDependenciesAsync(
                    orderedRefs,
                    mcVersion,
                    loaderSlug,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            resolved = [];
        }

        return new ModDependencyCheckResult
        {
            Missing = MergeDisplayItems(orderedRefs, resolved),
            MissingRefs = orderedRefs
        };
    }

    public async Task<IReadOnlyList<ModFileInstallRequest>> BuildInstallRequestsAsync(
        GameVersionItem instance,
        ModFileVersionItem primaryFile,
        string primaryFileName,
        string minecraftRoot,
        bool includeDependencies,
        IReadOnlyList<ModDependencyRef>? missingRefs,
        CancellationToken cancellationToken = default)
    {
        var requests = new List<ModFileInstallRequest>();
        var instanceDir = GamePaths.EnsureVersionIsolation(instance.Id, minecraftRoot);
        var modsDir = Path.Combine(instanceDir, "mods");
        Directory.CreateDirectory(modsDir);

        if (includeDependencies && missingRefs is { Count: > 0 })
        {
            var mcVersion = VersionKindDetector.DetectBaseGameVersion(instance.Id, minecraftRoot);
            var loaderSlug = ModInstanceMatcher.LoaderSlugForInstance(instance);

            foreach (var dep in missingRefs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pick = await ResolveDependencyFileAsync(
                        dep,
                        mcVersion,
                        loaderSlug,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (pick is null || string.IsNullOrWhiteSpace(pick.DownloadUrl))
                    continue;

                var fileName = SanitizeFileName(pick.FileName);
                if (!fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    fileName += ".jar";

                requests.Add(CreateRequest(
                    pick,
                    fileName,
                    instance.Id,
                    modsDir,
                    Loc.Format(LocKeys.Mod_InstallDependencyJobName, pick.DisplayName, instance.Id)));
            }
        }

        requests.Add(CreateRequest(
            primaryFile,
            primaryFileName,
            instance.Id,
            modsDir,
            Loc.Format(LocKeys.Mod_InstallJobName, primaryFile.DisplayName, instance.Id)));

        return requests;
    }

    private async Task<(List<ModDependencyRef> Missing, Dictionary<string, HashSet<string>> DependsOn)>
        CollectMissingTransitiveAsync(
            IReadOnlyList<ModDependencyRef> seedDeps,
            HashSet<string> localKeys,
            Dictionary<string, ModProjectItem?> projectCache,
            string mcVersion,
            string? loaderSlug,
            CancellationToken cancellationToken)
    {
        var missingMap = new Dictionary<string, ModDependencyRef>(StringComparer.OrdinalIgnoreCase);
        var dependsOn = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<ModDependencyRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dep in seedDeps)
        {
            var key = RefKey(dep);
            if (seen.Add(key))
                queue.Enqueue(dep);
        }

        while (queue.Count > 0 && missingMap.Count < MaxTransitiveDependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dep = queue.Dequeue();
            var key = RefKey(dep);

            if (await IsDependencyInstalledAsync(dep, localKeys, projectCache, cancellationToken)
                    .ConfigureAwait(false))
                continue;

            missingMap.TryAdd(key, dep);

            var childDeps = FilterDependencyCandidates(
                (await ResolveDependencyFileAsync(dep, mcVersion, loaderSlug, cancellationToken)
                    .ConfigureAwait(false))?.Dependencies ?? []);

            var childKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in childDeps)
            {
                var childKey = RefKey(child);
                childKeys.Add(childKey);
                if (seen.Add(childKey))
                    queue.Enqueue(child);
            }

            dependsOn[key] = childKeys;
        }

        return (missingMap.Values.ToList(), dependsOn);
    }

    private async Task<ModFileVersionItem?> ResolveDependencyFileAsync(
        ModDependencyRef dep,
        string mcVersion,
        string? loaderSlug,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _catalog.GetProjectDetailAsync(
                    ToProjectItem(dep),
                    cancellationToken,
                    mcVersion,
                    loaderSlug,
                    CatalogProjectKind.Mod)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(dep.VersionId))
            {
                var pinned = detail.Files.FirstOrDefault(f =>
                    string.Equals(f.Id, dep.VersionId, StringComparison.OrdinalIgnoreCase));
                if (pinned is not null && !string.IsNullOrWhiteSpace(pinned.DownloadUrl))
                    return pinned;
            }

            return ModFilePicker.PickBestCompatibleFile(detail.Files, mcVersion, loaderSlug);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> IsDependencyInstalledAsync(
        ModDependencyRef dep,
        HashSet<string> localKeys,
        Dictionary<string, ModProjectItem?> projectCache,
        CancellationToken cancellationToken)
    {
        if (MatchesLocalKeys(localKeys, dep))
            return true;

        var project = await GetCachedProjectAsync(dep, projectCache, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return false;

        if (!string.IsNullOrWhiteSpace(project.Slug) && localKeys.Contains(project.Slug))
            return true;
        if (!string.IsNullOrWhiteSpace(project.Id) && localKeys.Contains(project.Id))
            return true;

        return false;
    }

    private async Task<ModProjectItem?> GetCachedProjectAsync(
        ModDependencyRef dep,
        Dictionary<string, ModProjectItem?> projectCache,
        CancellationToken cancellationToken)
    {
        var cacheKey = RefKey(dep);
        if (projectCache.TryGetValue(cacheKey, out var cached))
            return cached;

        ModProjectItem? project = null;
        try
        {
            if (string.Equals(dep.SourceId, ModSearchViewModel.SourceIdModrinth, StringComparison.OrdinalIgnoreCase))
            {
                project = await _catalog.TryGetModrinthProjectAsync(
                    dep.ProjectId,
                    CatalogProjectKind.Mod,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            project = null;
        }

        projectCache[cacheKey] = project;
        return project;
    }

    private static bool MatchesLocalKeys(HashSet<string> localKeys, ModDependencyRef dep)
    {
        if (localKeys.Contains(dep.ProjectId))
            return true;
        if (localKeys.Contains(ProjectKey(dep.SourceId, dep.ProjectId)))
            return true;
        if (!string.IsNullOrWhiteSpace(dep.VersionId) && localKeys.Contains(dep.VersionId))
            return true;
        return false;
    }

    private static HashSet<string> BuildLocalInstalledKeys(string modsDir)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(modsDir))
            return keys;

        foreach (var meta in ModJarMetadataReader.ScanModsFolder(modsDir))
        {
            if (!string.IsNullOrWhiteSpace(meta.ModId))
                keys.Add(meta.ModId);
        }

        return keys;
    }

    private static List<ModDependencyRef> SortDependenciesFirst(
        IReadOnlyList<ModDependencyRef> missing,
        Dictionary<string, HashSet<string>> dependsOn)
    {
        var keyToRef = missing.ToDictionary(RefKey, r => r, StringComparer.OrdinalIgnoreCase);
        var remaining = new HashSet<string>(keyToRef.Keys, StringComparer.OrdinalIgnoreCase);
        var result = new List<ModDependencyRef>(missing.Count);

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(key => !dependsOn.TryGetValue(key, out var deps) ||
                              deps.All(depKey => !remaining.Contains(depKey)))
                .OrderBy(key => keyToRef[key].Kind)
                .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ready.Count == 0)
                ready = remaining.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var key in ready)
            {
                result.Add(keyToRef[key]);
                remaining.Remove(key);
            }
        }

        return result;
    }

    private static IReadOnlyList<ModDependencyRef> FilterDependencyCandidates(
        IReadOnlyList<ModDependencyRef> dependencies) =>
        dependencies
            .Where(d => d.Kind is ModDependencyKind.Required or ModDependencyKind.Optional)
            .Where(d => !string.IsNullOrWhiteSpace(d.ProjectId))
            .GroupBy(d => RefKey(d), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(d => d.Kind).First())
            .ToList();

    private static IReadOnlyList<ModDependencyItem> MergeDisplayItems(
        IReadOnlyList<ModDependencyRef> missingRefs,
        IReadOnlyList<ModDependencyItem> resolved)
    {
        var byKey = resolved.ToDictionary(
            item => RefKey(item.SourceId, item.Id),
            StringComparer.OrdinalIgnoreCase);

        return missingRefs
            .Select(r =>
            {
                var key = RefKey(r);
                if (byKey.TryGetValue(key, out var item))
                    return item;

                return new ModDependencyItem
                {
                    Id = r.ProjectId,
                    SourceId = r.SourceId,
                    Title = r.ProjectId,
                    VersionsLabel = string.Empty,
                    LoadersLabel = string.Empty,
                    Kind = r.Kind
                };
            })
            .ToList();
    }

    private static ModDependencyCheckResult EmptyResult() =>
        new() { Missing = [], MissingRefs = [] };

    private static ModProjectItem ToProjectItem(ModDependencyRef dep) =>
        new()
        {
            Id = dep.ProjectId,
            SourceId = dep.SourceId,
            Title = dep.ProjectId,
            Description = string.Empty,
            SourceLabel = string.Empty,
            Downloads = 0,
            DownloadsLabel = string.Empty,
            VersionsLabel = string.Empty,
            LoadersLabel = string.Empty
        };

    private static ModFileInstallRequest CreateRequest(
        ModFileVersionItem file,
        string fileName,
        string instanceId,
        string modsDir,
        string displayName) =>
        new()
        {
            DisplayName = displayName,
            FileName = fileName,
            DownloadUrl = file.DownloadUrl,
            TargetInstanceId = instanceId,
            ModsDirectory = modsDir
        };

    private static string RefKey(ModDependencyRef dep) => RefKey(dep.SourceId, dep.ProjectId);

    private static string RefKey(string sourceId, string projectId) =>
        $"{sourceId}|{projectId}";

    private static string ProjectKey(string sourceId, string projectId) =>
        $"{sourceId}|{projectId}";

    private static string SanitizeFileName(string name)
    {
        var trimmed = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(c, '_');
        return string.IsNullOrWhiteSpace(trimmed) ? "mod.jar" : trimmed;
    }
}
