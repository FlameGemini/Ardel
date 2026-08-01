using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Microsoft.UI.Xaml;

namespace Ardel.Launcher.Helpers;

/// <summary>One loader + Minecraft version bucket of install targets.</summary>
public sealed class ModInstanceGroup : List<GameVersionItem>
{
    public ModInstanceGroup(string key, string header, bool isPreferred)
    {
        Key = key;
        Header = header;
        IsPreferred = isPreferred;
    }

    public string Key { get; }
    public string Header { get; }
    public bool IsPreferred { get; }
    public Visibility PreferredBadgeVisibility =>
        IsPreferred ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// Builds install targets for a Mod file: requires game version + loader fit,
/// never offers vanilla, and groups by loader + base Minecraft version.
/// </summary>
public static class ModInstanceMatcher
{
    public static IReadOnlyList<ModInstanceGroup> BuildGroups(
        IReadOnlyList<GameVersionItem> instances,
        ModFileVersionItem file,
        string? minecraftRoot,
        string? preferredGameVersion = null,
        string? preferredLoaderSlug = null,
        CatalogProjectKind kind = CatalogProjectKind.Mod)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(file);

        if (kind is CatalogProjectKind.ResourcePack or CatalogProjectKind.Datapack
            or CatalogProjectKind.ShaderPack or CatalogProjectKind.Modpack)
            return BuildPackGroups(instances, file, minecraftRoot, preferredGameVersion);

        var buckets = new Dictionary<string, (string Header, bool Preferred, List<GameVersionItem> Items)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var instance in instances)
        {
            if (!IsCompatible(instance, file, minecraftRoot))
                continue;

            var baseMc = GamePaths.ResolveBaseGameVersion(instance.Id, minecraftRoot);
            if (string.IsNullOrWhiteSpace(baseMc))
                baseMc = instance.Id;

            var loaderSlug = LoaderSlug(instance);
            if (loaderSlug is null)
                continue;

            var key = loaderSlug + "|" + baseMc;
            var preferred = IsPreferred(baseMc, loaderSlug, preferredGameVersion, preferredLoaderSlug);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                var header = FormatGroupHeader(loaderSlug, baseMc);
                bucket = (header, preferred, []);
                buckets[key] = bucket;
            }
            else if (preferred && !bucket.Preferred)
            {
                bucket = (bucket.Header, true, bucket.Items);
                buckets[key] = bucket;
            }

            bucket.Items.Add(instance);
        }

        return buckets
            .Select(kv =>
            {
                var (header, preferred, items) = kv.Value;
                items.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
                var group = new ModInstanceGroup(kv.Key, header, preferred);
                group.AddRange(items);
                return group;
            })
            .OrderByDescending(g => PreferenceScore(g, preferredGameVersion, preferredLoaderSlug))
            .ThenByDescending(g => GroupGameVersion(g.Key), MinecraftVersionOrder.Ascending)
            .ThenBy(g => g.Header, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ModInstanceGroup> BuildPackGroups(
        IReadOnlyList<GameVersionItem> instances,
        ModFileVersionItem file,
        string? minecraftRoot,
        string? preferredGameVersion)
    {
        var buckets = new Dictionary<string, (string Header, bool Preferred, List<GameVersionItem> Items)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var instance in instances)
        {
            if (file.GameVersions.Count > 0 &&
                !MatchesGameVersion(instance, file.GameVersions, minecraftRoot))
                continue;

            var baseMc = GamePaths.ResolveBaseGameVersion(instance.Id, minecraftRoot);
            if (string.IsNullOrWhiteSpace(baseMc))
                baseMc = instance.Id;

            var preferred = !string.IsNullOrWhiteSpace(preferredGameVersion) &&
                            string.Equals(baseMc, preferredGameVersion.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!buckets.TryGetValue(baseMc, out var bucket))
            {
                bucket = (baseMc, preferred, []);
                buckets[baseMc] = bucket;
            }
            else if (preferred && !bucket.Preferred)
            {
                bucket = (bucket.Header, true, bucket.Items);
                buckets[baseMc] = bucket;
            }

            bucket.Items.Add(instance);
        }

        return buckets
            .Select(kv =>
            {
                var (header, preferred, items) = kv.Value;
                items.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
                var group = new ModInstanceGroup(kv.Key, header, preferred);
                group.AddRange(items);
                return group;
            })
            .OrderByDescending(g => g.IsPreferred)
            .ThenByDescending(g => g.Header, MinecraftVersionOrder.Ascending)
            .ThenBy(g => g.Header, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Group keys are <c>loader|gameVersion</c>.</summary>
    private static string GroupGameVersion(string key)
    {
        var sep = key.IndexOf('|');
        return sep >= 0 && sep < key.Length - 1 ? key[(sep + 1)..] : key;
    }

    public static bool IsCompatible(
        GameVersionItem instance,
        ModFileVersionItem file,
        string? minecraftRoot)
    {
        // Mods go into a loader instance — never vanilla / OptiFine-only profiles.
        if (instance.Kind is VersionKind.Vanilla or VersionKind.OptiFine)
            return false;

        if (LoaderSlug(instance) is null)
            return false;

        if (file.GameVersions.Count > 0 &&
            !MatchesGameVersion(instance, file.GameVersions, minecraftRoot))
            return false;

        if (file.Loaders.Count > 0 &&
            !MatchesLoader(instance, file.Loaders))
            return false;

        // File omitted loader metadata: still require a real loader instance.
        return true;
    }

    public static string? LoaderIdToSlug(string? loaderId) => loaderId switch
    {
        "1" => "forge",
        "16" => "neoforge",
        "4" => "fabric",
        "8" => "quilt",
        "2" => "liteloader",
        _ => null
    };

    public static string FormatGroupHeader(string loaderSlug, string gameVersion)
    {
        var loader = loaderSlug.ToLowerInvariant() switch
        {
            "forge" => Loc.Get(LocKeys.Mod_LoaderForge),
            "neoforge" => Loc.Get(LocKeys.Mod_LoaderNeoForge),
            "fabric" => Loc.Get(LocKeys.Mod_LoaderFabric),
            "quilt" => Loc.Get(LocKeys.Mod_LoaderQuilt),
            "liteloader" => Loc.Get(LocKeys.Mod_LoaderLiteLoader),
            _ => loaderSlug
        };
        return loader + gameVersion;
    }

    private static int PreferenceScore(
        ModInstanceGroup group,
        string? preferredGameVersion,
        string? preferredLoaderSlug)
    {
        var parts = group.Key.Split('|');
        var loader = parts.Length > 0 ? parts[0] : string.Empty;
        var game = parts.Length > 1 ? parts[1] : string.Empty;
        return PreferenceScore(game, loader, preferredGameVersion, preferredLoaderSlug);
    }

    private static int PreferenceScore(
        string gameVersion,
        string loaderSlug,
        string? preferredGameVersion,
        string? preferredLoaderSlug)
    {
        var gameHit = !string.IsNullOrWhiteSpace(preferredGameVersion) &&
                      string.Equals(gameVersion, preferredGameVersion.Trim(), StringComparison.OrdinalIgnoreCase);
        var loaderHit = !string.IsNullOrWhiteSpace(preferredLoaderSlug) &&
                        string.Equals(loaderSlug, preferredLoaderSlug.Trim(), StringComparison.OrdinalIgnoreCase);

        if (gameHit && loaderHit)
            return 3;
        if (gameHit)
            return 2;
        if (loaderHit)
            return 1;
        return 0;
    }

    private static bool IsPreferred(
        string gameVersion,
        string loaderSlug,
        string? preferredGameVersion,
        string? preferredLoaderSlug) =>
        PreferenceScore(gameVersion, loaderSlug, preferredGameVersion, preferredLoaderSlug) > 0;

    private static string? LoaderSlug(GameVersionItem instance) => instance.Kind switch
    {
        VersionKind.Forge => "forge",
        VersionKind.NeoForge => "neoforge",
        VersionKind.Fabric => "fabric",
        VersionKind.Custom when instance.Id.Contains("liteloader", StringComparison.OrdinalIgnoreCase) =>
            "liteloader",
        VersionKind.Custom => "quilt",
        _ => null
    };

    private static bool MatchesGameVersion(
        GameVersionItem instance,
        IReadOnlyList<string> gameVersions,
        string? minecraftRoot)
    {
        var baseId = GamePaths.ResolveBaseGameVersion(instance.Id, minecraftRoot);
        foreach (var gv in gameVersions)
        {
            if (string.IsNullOrWhiteSpace(gv))
                continue;
            var trimmed = gv.Trim();
            if (string.Equals(trimmed, baseId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, instance.Id, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool MatchesLoader(GameVersionItem instance, IReadOnlyList<string> loaders)
    {
        var instanceSlug = LoaderSlug(instance);
        if (instanceSlug is null)
            return false;

        foreach (var raw in loaders)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var slug = raw.Trim().ToLowerInvariant();
            if (string.Equals(slug, instanceSlug, StringComparison.OrdinalIgnoreCase))
                return true;

            // Quilt mods often run on Fabric instances.
            if (slug == "quilt" && instanceSlug == "fabric")
                return true;
        }

        return false;
    }
}
