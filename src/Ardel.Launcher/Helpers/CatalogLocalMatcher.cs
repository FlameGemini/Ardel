using System.Collections.Concurrent;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Resolves a local jar/zip to a catalog project. Prefers Modrinth file hash and slug
/// over "first search hit", which often maps the wrong project.
/// </summary>
public static class CatalogLocalMatcher
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(8);

    private readonly record struct CacheEntry(ModProjectItem? Item, long Ticks);

    public static async Task<ModProjectItem?> MatchAsync(
        ModCatalogService catalog,
        string fullPath,
        string fileName,
        string? localTitle,
        string? localId,
        CatalogProjectKind kind,
        CancellationToken cancellationToken)
    {
        string? stamp = null;
        if (File.Exists(fullPath))
        {
            try
            {
                var info = new FileInfo(fullPath);
                stamp = $"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{kind}";
                if (TryGetCached(stamp, out var cached))
                    return cached;
            }
            catch
            {
                stamp = null;
            }
        }

        var match = await MatchUncachedAsync(
            catalog, fullPath, fileName, localTitle, localId, kind, cancellationToken)
            .ConfigureAwait(false);

        if (stamp is not null)
            Cache[stamp] = new CacheEntry(match, Environment.TickCount64);

        return match;
    }

    private static bool TryGetCached(string key, out ModProjectItem? item)
    {
        item = null;
        if (!Cache.TryGetValue(key, out var entry))
            return false;
        if (Environment.TickCount64 - entry.Ticks > Ttl.TotalMilliseconds)
        {
            Cache.TryRemove(key, out _);
            return false;
        }

        item = entry.Item;
        return true;
    }

    private static async Task<ModProjectItem?> MatchUncachedAsync(
        ModCatalogService catalog,
        string fullPath,
        string fileName,
        string? localTitle,
        string? localId,
        CatalogProjectKind kind,
        CancellationToken cancellationToken)
    {
        if (File.Exists(fullPath))
        {
            var sha1 = await Task.Run(() => TrySha1(fullPath), cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(sha1))
            {
                var byHash = await catalog.FindByFileHashAsync(sha1, kind, cancellationToken).ConfigureAwait(false);
                if (byHash is not null)
                    return byHash;
            }
        }

        foreach (var slug in SlugCandidates(localId, fileName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bySlug = await catalog.TryGetModrinthProjectAsync(slug, kind, cancellationToken).ConfigureAwait(false);
            if (bySlug is not null)
                return bySlug;
        }

        var query = BestSearchQuery(localTitle, localId, fileName);
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var stem = FileStem(fileName);
        var scored = await SearchBestAsync(
                catalog, query, localTitle, localId, stem, kind, ModSearchViewModel.SourceIdModrinth, cancellationToken)
            .ConfigureAwait(false);
        if (scored is not null)
            return scored;

        try
        {
            return await SearchBestAsync(
                    catalog, query, localTitle, localId, stem, kind, ModSearchViewModel.SourceIdCurseForge, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ModProjectItem?> SearchBestAsync(
        ModCatalogService catalog,
        string query,
        string? localTitle,
        string? localId,
        string fileStem,
        CatalogProjectKind kind,
        string sourceId,
        CancellationToken cancellationToken)
    {
        var result = await catalog.SearchAsync(new ModSearchCriteria(
            Keyword: query,
            SourceId: sourceId,
            GameVersion: string.Empty,
            CategoryId: string.Empty,
            LoaderId: string.Empty,
            Kind: kind
        ), offset: 0, cancellationToken: cancellationToken).ConfigureAwait(false);

        return PickBest(result.Items, query, localTitle, localId, fileStem);
    }

    internal static ModProjectItem? PickBest(
        IReadOnlyList<ModProjectItem> hits,
        string query,
        string? localTitle,
        string? localId,
        string fileStem)
    {
        ModProjectItem? best = null;
        var bestScore = 0;
        foreach (var hit in hits)
        {
            var score = Score(hit, query, localTitle, localId, fileStem);
            if (score > bestScore)
            {
                bestScore = score;
                best = hit;
            }
        }

        return bestScore >= 70 ? best : null;
    }

    private static int Score(
        ModProjectItem hit,
        string query,
        string? localTitle,
        string? localId,
        string fileStem)
    {
        var title = hit.Title.Trim();
        var slug = (hit.Slug ?? string.Empty).Trim();
        var qNorm = Normalize(query);
        var titleNorm = Normalize(title);
        var slugNorm = Normalize(slug);
        var stemNorm = Normalize(fileStem);
        var localNorm = Normalize(localTitle ?? string.Empty);
        var idNorm = Normalize(localId ?? string.Empty);

        var score = 0;
        if (idNorm.Length > 0 && (slugNorm == idNorm || Normalize(hit.Id) == idNorm))
            score = Math.Max(score, 100);
        if (localNorm.Length > 0 && titleNorm == localNorm)
            score = Math.Max(score, 96);
        if (qNorm.Length > 0 && titleNorm == qNorm)
            score = Math.Max(score, 94);
        if (qNorm.Length > 0 && slugNorm == qNorm)
            score = Math.Max(score, 92);
        if (stemNorm.Length >= 4 && slugNorm == stemNorm)
            score = Math.Max(score, 90);
        if (qNorm.Length >= 4 && titleNorm.StartsWith(qNorm, StringComparison.Ordinal))
            score = Math.Max(score, 78);
        if (qNorm.Length >= 5 && titleNorm.Contains(qNorm, StringComparison.Ordinal))
            score = Math.Max(score, 72);

        if (qNorm.Length < 4 && score < 90)
            return 0;

        return score;
    }

    private static IEnumerable<string> SlugCandidates(string? localId, string fileName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Offer(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var slug = value.Trim().Replace('_', '-');
            if (slug.Length < 2 || slug.Contains(' ') || !seen.Add(slug))
                return;
        }

        Offer(localId);
        Offer(StripVersionSuffix(FileStem(fileName)));
        return seen;
    }

    private static string BestSearchQuery(string? localTitle, string? localId, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(localTitle) && localTitle.Trim().Length >= 3)
            return localTitle.Trim();
        if (!string.IsNullOrWhiteSpace(localId) && localId.Trim().Length >= 3)
            return localId.Trim().Replace('_', ' ');

        var stem = StripVersionSuffix(FileStem(fileName));
        stem = System.Text.RegularExpressions.Regex.Replace(
            stem,
            @"[-_](fabric|forge|neoforge|quilt|iris|optifine)$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(stem) ? FileStem(fileName) : stem;
    }

    private static string FileStem(string fileName)
    {
        var name = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".disabled".Length]
            : fileName;
        return Path.GetFileNameWithoutExtension(name);
    }

    private static string StripVersionSuffix(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"[-_][vV]?\d.*$", string.Empty).Trim();

    private static string Normalize(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, @"[^a-z0-9]+", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ToLowerInvariant();

    private static string? TrySha1(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hash = System.Security.Cryptography.SHA1.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}
