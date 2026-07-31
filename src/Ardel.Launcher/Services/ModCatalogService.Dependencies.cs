using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using Ardel.Launcher.Models;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Services;

public sealed partial class ModCatalogService
{
    /// <summary>
    /// Resolve required dependency projects for display (icon / name / version / loader).
    /// </summary>
    public async Task<IReadOnlyList<ModDependencyItem>> ResolveDependenciesAsync(
        IReadOnlyList<ModDependencyRef> refs,
        string? preferredGameVersion,
        string? preferredLoaderSlug,
        CancellationToken cancellationToken = default)
    {
        if (refs.Count == 0)
            return [];

        var unique = refs
            .Where(r => !string.IsNullOrWhiteSpace(r.ProjectId))
            .GroupBy(r => r.SourceId + "|" + r.ProjectId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(24)
            .ToList();

        var tasks = unique.Select(r => ResolveOneDependencyAsync(
            r,
            preferredGameVersion,
            preferredLoaderSlug,
            cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(x => x is not null).Select(x => x!).ToList();
    }

    private async Task<ModDependencyItem?> ResolveOneDependencyAsync(
        ModDependencyRef dep,
        string? preferredGameVersion,
        string? preferredLoaderSlug,
        CancellationToken cancellationToken)
    {
        try
        {
            return dep.SourceId switch
            {
                ModSearchViewModel.SourceIdModrinth =>
                    await ResolveModrinthDependencyAsync(dep, preferredGameVersion, preferredLoaderSlug, cancellationToken)
                        .ConfigureAwait(false),
                ModSearchViewModel.SourceIdCurseForge =>
                    await ResolveCurseForgeDependencyAsync(dep, preferredGameVersion, preferredLoaderSlug, cancellationToken)
                        .ConfigureAwait(false),
                _ => null
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<ModDependencyItem?> ResolveModrinthDependencyAsync(
        ModDependencyRef dep,
        string? preferredGameVersion,
        string? preferredLoaderSlug,
        CancellationToken cancellationToken)
    {
        using var projectResponse = await _http
            .GetAsync($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(dep.ProjectId)}", cancellationToken)
            .ConfigureAwait(false);
        if (!projectResponse.IsSuccessStatusCode)
            return null;

        await using var projectStream = await projectResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var projectDoc = await JsonDocument.ParseAsync(projectStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = projectDoc.RootElement;

        var title = root.TryGetProperty("title", out var titleEl)
            ? titleEl.GetString()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var iconUrl = root.TryGetProperty("icon_url", out var iconEl) ? iconEl.GetString() : null;

        string versionsLabel = string.Empty;
        string loadersLabel = string.Empty;

        if (!string.IsNullOrWhiteSpace(dep.VersionId))
        {
            using var verResponse = await _http
                .GetAsync($"https://api.modrinth.com/v2/version/{Uri.EscapeDataString(dep.VersionId)}", cancellationToken)
                .ConfigureAwait(false);
            if (verResponse.IsSuccessStatusCode)
            {
                var ver = await verResponse.Content
                    .ReadFromJsonAsync<ModrinthVersionDto>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (ver is not null)
                {
                    versionsLabel = FormatVersions(ver.GameVersions ?? [], preferredGameVersion);
                    loadersLabel = FormatLoaders(ver.Loaders ?? []);
                    if (string.IsNullOrWhiteSpace(versionsLabel) &&
                        !string.IsNullOrWhiteSpace(ver.VersionNumber))
                        versionsLabel = ver.VersionNumber.Trim();
                }
            }
        }

        if (string.IsNullOrEmpty(versionsLabel) || string.IsNullOrEmpty(loadersLabel))
        {
            var url = new StringBuilder("https://api.modrinth.com/v2/project/")
                .Append(Uri.EscapeDataString(dep.ProjectId))
                .Append("/version?limit=20");
            if (!string.IsNullOrWhiteSpace(preferredLoaderSlug))
            {
                url.Append("&loaders=")
                    .Append(Uri.EscapeDataString($"[\"{preferredLoaderSlug}\"]"));
            }

            if (!string.IsNullOrWhiteSpace(preferredGameVersion))
            {
                url.Append("&game_versions=")
                    .Append(Uri.EscapeDataString($"[\"{preferredGameVersion}\"]"));
            }

            using var listResponse = await _http.GetAsync(url.ToString(), cancellationToken).ConfigureAwait(false);
            if (listResponse.IsSuccessStatusCode)
            {
                var versions = await listResponse.Content
                    .ReadFromJsonAsync<List<ModrinthVersionDto>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                var best = versions?
                    .OrderByDescending(v => string.Equals(v.VersionType, "release", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(v => v.DatePublished ?? DateTimeOffset.MinValue)
                    .FirstOrDefault();
                if (best is not null)
                {
                    if (string.IsNullOrEmpty(versionsLabel))
                        versionsLabel = FormatVersions(best.GameVersions ?? [], preferredGameVersion);
                    if (string.IsNullOrEmpty(loadersLabel))
                        loadersLabel = FormatLoaders(best.Loaders ?? []);
                    if (string.IsNullOrEmpty(versionsLabel) && !string.IsNullOrWhiteSpace(best.VersionNumber))
                        versionsLabel = best.VersionNumber.Trim();
                }
            }
        }

        if (string.IsNullOrEmpty(versionsLabel) &&
            root.TryGetProperty("game_versions", out var gv) &&
            gv.ValueKind == JsonValueKind.Array)
        {
            versionsLabel = FormatVersions(
                gv.EnumerateArray().Select(e => e.GetString() ?? string.Empty),
                preferredGameVersion);
        }

        if (string.IsNullOrEmpty(loadersLabel) &&
            root.TryGetProperty("loaders", out var ld) &&
            ld.ValueKind == JsonValueKind.Array)
        {
            loadersLabel = FormatLoaders(
                ld.EnumerateArray().Select(e => e.GetString() ?? string.Empty));
        }

        return CreateDependencyItem(
            dep.ProjectId,
            ModSearchViewModel.SourceIdModrinth,
            title,
            versionsLabel,
            loadersLabel,
            iconUrl);
    }

    private async Task<ModDependencyItem?> ResolveCurseForgeDependencyAsync(
        ModDependencyRef dep,
        string? preferredGameVersion,
        string? preferredLoaderSlug,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(dep.ProjectId, out var modId))
            return null;

        using var response = await _http
            .GetAsync($"{CurseForgeApiBase}/mods/{modId}", cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return null;

        var title = data.TryGetProperty("name", out var nameEl) ? nameEl.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(title))
            return null;

        string? iconUrl = null;
        if (data.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object)
        {
            if (logo.TryGetProperty("thumbnailUrl", out var thumb))
                iconUrl = thumb.GetString();
            if (string.IsNullOrWhiteSpace(iconUrl) && logo.TryGetProperty("url", out var urlEl))
                iconUrl = urlEl.GetString();
        }

        var versions = new List<string>();
        var loaders = new List<string>();
        if (data.TryGetProperty("latestFilesIndexes", out var indexes) &&
            indexes.ValueKind == JsonValueKind.Array)
        {
            foreach (var idx in indexes.EnumerateArray())
            {
                if (idx.TryGetProperty("gameVersion", out var gv) &&
                    gv.GetString() is { Length: > 0 } version)
                    versions.Add(version);

                if (idx.TryGetProperty("modLoader", out var ml) && ml.TryGetInt32(out var code))
                {
                    var slug = CurseForgeLoaderSlug(code);
                    if (slug is not null)
                        loaders.Add(slug);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredGameVersion))
        {
            versions = versions
                .OrderByDescending(v => string.Equals(v, preferredGameVersion, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(preferredLoaderSlug))
        {
            loaders = loaders
                .OrderByDescending(l => string.Equals(l, preferredLoaderSlug, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return CreateDependencyItem(
            dep.ProjectId,
            ModSearchViewModel.SourceIdCurseForge,
            title,
            FormatVersions(versions, preferredGameVersion),
            FormatLoaders(loaders),
            iconUrl);
    }

    private static ModDependencyItem CreateDependencyItem(
        string id,
        string sourceId,
        string title,
        string versionsLabel,
        string loadersLabel,
        string? iconUrl)
    {
        Uri? iconUri = null;
        if (!string.IsNullOrWhiteSpace(iconUrl) &&
            Uri.TryCreate(iconUrl.Trim(), UriKind.Absolute, out var remote) &&
            (remote.Scheme == Uri.UriSchemeHttp || remote.Scheme == Uri.UriSchemeHttps))
        {
            iconUri = remote;
        }

        return new ModDependencyItem
        {
            Id = id,
            SourceId = sourceId,
            Title = title,
            VersionsLabel = versionsLabel,
            LoadersLabel = loadersLabel,
            IconUrl = iconUrl,
            IconUri = iconUri
        };
    }

    private static IReadOnlyList<ModDependencyRef> ParseModrinthDependencies(
        List<ModrinthDependencyDto>? dependencies)
    {
        if (dependencies is null || dependencies.Count == 0)
            return [];

        var list = new List<ModDependencyRef>();
        foreach (var dep in dependencies)
        {
            if (!string.Equals(dep.DependencyType, "required", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(dep.ProjectId))
                continue;

            list.Add(new ModDependencyRef
            {
                ProjectId = dep.ProjectId.Trim(),
                VersionId = string.IsNullOrWhiteSpace(dep.VersionId) ? null : dep.VersionId.Trim(),
                SourceId = ModSearchViewModel.SourceIdModrinth
            });
        }

        return list;
    }

    private static IReadOnlyList<ModDependencyRef> ParseCurseForgeDependencies(JsonElement file)
    {
        if (!file.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<ModDependencyRef>();
        foreach (var dep in deps.EnumerateArray())
        {
            // 3 = RequiredDependency
            if (!dep.TryGetProperty("relationType", out var rel) ||
                !rel.TryGetInt32(out var relationType) ||
                relationType != 3)
                continue;

            if (!dep.TryGetProperty("modId", out var modIdEl))
                continue;

            var modId = modIdEl.ValueKind == JsonValueKind.Number
                ? modIdEl.GetInt64().ToString()
                : modIdEl.GetString();
            if (string.IsNullOrWhiteSpace(modId))
                continue;

            list.Add(new ModDependencyRef
            {
                ProjectId = modId.Trim(),
                SourceId = ModSearchViewModel.SourceIdCurseForge
            });
        }

        return list;
    }
}
