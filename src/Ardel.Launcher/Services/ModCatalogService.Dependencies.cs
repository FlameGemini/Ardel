using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Services;

public sealed partial class ModCatalogService
{
    /// <summary>
    /// Return dependency refs for a catalog file version, fetching from the API when the cached file has none.
    /// </summary>
    public async Task<IReadOnlyList<ModDependencyRef>> GetFileVersionDependenciesAsync(
        string sourceId,
        ModFileVersionItem file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var cached = file.Dependencies
            .Where(d => d.Kind is ModDependencyKind.Required or ModDependencyKind.Optional)
            .Where(d => !string.IsNullOrWhiteSpace(d.ProjectId))
            .ToList();
        if (cached.Count > 0)
            return cached;

        if (string.IsNullOrWhiteSpace(file.Id))
            return [];

        return sourceId switch
        {
            ModSearchViewModel.SourceIdModrinth =>
                await GetModrinthVersionDependenciesAsync(file.Id, cancellationToken).ConfigureAwait(false),
            ModSearchViewModel.SourceIdCurseForge =>
                await GetCurseForgeFileDependenciesAsync(file.Id, cancellationToken).ConfigureAwait(false),
            _ => []
        };
    }

    private async Task<IReadOnlyList<ModDependencyRef>> GetModrinthVersionDependenciesAsync(
        string versionId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http
                .GetAsync(
                    $"https://api.modrinth.com/v2/version/{Uri.EscapeDataString(versionId)}",
                    cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return [];

            var ver = await response.Content
                .ReadFromJsonAsync<ModrinthVersionDto>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return ParseModrinthDependencies(ver?.Dependencies);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<ModDependencyRef>> GetCurseForgeFileDependenciesAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(fileId, out var numericId))
            return [];

        try
        {
            using var response = await _http
                .GetAsync($"{CurseForgeApiBase}/mods/files/{numericId}", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return [];

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return [];

            return ParseCurseForgeDependencies(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

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
            .Where(r => r.Kind is ModDependencyKind.Required or ModDependencyKind.Optional)
            .GroupBy(r => r.SourceId + "|" + r.ProjectId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderBy(r => r.Kind == ModDependencyKind.Required ? 0 : 1)
                .First())
            .Take(24)
            .ToList();

        var tasks = unique.Select(r => ResolveOneDependencyAsync(
            r,
            preferredGameVersion,
            preferredLoaderSlug,
            cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(x => x.Kind == ModDependencyKind.Required ? 0 : 1)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            iconUrl,
            dep.Kind);
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
            iconUrl,
            dep.Kind);
    }

    private static ModDependencyItem CreateDependencyItem(
        string id,
        string sourceId,
        string title,
        string versionsLabel,
        string loadersLabel,
        string? iconUrl,
        ModDependencyKind kind)
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
            Kind = kind,
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
            var kind = ParseModrinthDependencyKind(dep.DependencyType);
            if (kind is null || string.IsNullOrWhiteSpace(dep.ProjectId))
                continue;

            list.Add(new ModDependencyRef
            {
                ProjectId = dep.ProjectId.Trim(),
                VersionId = string.IsNullOrWhiteSpace(dep.VersionId) ? null : dep.VersionId.Trim(),
                SourceId = ModSearchViewModel.SourceIdModrinth,
                Kind = kind.Value
            });
        }

        return list;
    }

    private static ModDependencyKind? ParseModrinthDependencyKind(string? type) =>
        type?.Trim().ToLowerInvariant() switch
        {
            "required" => ModDependencyKind.Required,
            "optional" => ModDependencyKind.Optional,
            "embedded" => ModDependencyKind.Embedded,
            _ => null
        };

    private static IReadOnlyList<ModDependencyRef> ParseCurseForgeDependencies(JsonElement file)
    {
        if (!file.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<ModDependencyRef>();
        foreach (var dep in deps.EnumerateArray())
        {
            // 2 = OptionalDependency, 3 = RequiredDependency
            if (!dep.TryGetProperty("relationType", out var rel) ||
                !rel.TryGetInt32(out var relationType))
                continue;

            ModDependencyKind kind;
            // 1 EmbeddedLibrary, 2 Optional, 3 Required, 6 Include (pack contents)
            if (relationType is 3)
                kind = ModDependencyKind.Required;
            else if (relationType is 2)
                kind = ModDependencyKind.Optional;
            else if (relationType is 1 or 6)
                kind = ModDependencyKind.Embedded;
            else
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
                SourceId = ModSearchViewModel.SourceIdCurseForge,
                Kind = kind
            });
        }

        return list;
    }

    private async Task<List<ModFileVersionItem>> EnrichModrinthPackContentsAsync(
        List<ModFileVersionItem> files,
        CancellationToken cancellationToken)
    {
        var ids = files
            .SelectMany(f => f.Dependencies)
            .Where(IsPackContentDependency)
            .Select(d => d.ProjectId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();

        var projects = await FetchModrinthProjectSummariesAsync(ids, cancellationToken).ConfigureAwait(false);

        return files.Select(file =>
        {
            var contents = file.Dependencies
                .Where(IsPackContentDependency)
                .Select(d => projects.TryGetValue(d.ProjectId, out var item)
                    ? item
                    : CreateFallbackContent(d.ProjectId, ModSearchViewModel.SourceIdModrinth, d.ProjectId))
                .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return WithIncludedMods(file, contents);
        }).ToList();
    }

    private static bool IsPackContentDependency(ModDependencyRef d) =>
        d.Kind is ModDependencyKind.Embedded
            or ModDependencyKind.Required
            or ModDependencyKind.Optional;

    private async Task<Dictionary<string, ModpackContentItem>> FetchModrinthProjectSummariesAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, ModpackContentItem>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return map;

        try
        {
            var payload = JsonSerializer.Serialize(ids);
            var url = $"https://api.modrinth.com/v2/projects?ids={Uri.EscapeDataString(payload)}";
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return map;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return map;

            foreach (var project in doc.RootElement.EnumerateArray())
            {
                var id = project.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var title = project.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                    continue;

                var slug = project.TryGetProperty("slug", out var slugEl) ? slugEl.GetString() : null;
                var iconUrl = project.TryGetProperty("icon_url", out var iconEl) ? iconEl.GetString() : null;
                var projectType = project.TryGetProperty("project_type", out var typeEl)
                    ? typeEl.GetString()
                    : "mod";
                var typePath = string.IsNullOrWhiteSpace(projectType) ? "mod" : projectType.Trim();
                var web = !string.IsNullOrWhiteSpace(slug)
                    ? $"https://modrinth.com/{typePath}/{slug.Trim()}"
                    : $"https://modrinth.com/{typePath}/{id.Trim()}";

                map[id] = CreateContentItem(
                    id.Trim(),
                    ModSearchViewModel.SourceIdModrinth,
                    title.Trim(),
                    iconUrl,
                    web,
                    slug);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort identification — pack install still works without titles.
        }

        return map;
    }

    private async Task<List<ModFileVersionItem>> EnrichCurseForgePackContentsAsync(
        List<ModFileVersionItem> files,
        CancellationToken cancellationToken)
    {
        var ids = files
            .SelectMany(f => f.Dependencies)
            .Where(IsPackContentDependency)
            .Select(d => d.ProjectId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();

        var projects = await FetchCurseForgeProjectSummariesAsync(ids, cancellationToken).ConfigureAwait(false);

        return files.Select(file =>
        {
            var fromDeps = file.Dependencies
                .Where(IsPackContentDependency)
                .Select(d => projects.TryGetValue(d.ProjectId, out var item) ? item : null)
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();

            var fromModules = file.IncludedMods
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Where(n => fromDeps.All(d => !string.Equals(d.Title, n, StringComparison.OrdinalIgnoreCase)))
                .Select(n => CreateFallbackContent(
                    "module:" + n,
                    ModSearchViewModel.SourceIdCurseForge,
                    n,
                    canOpenInLauncher: false));

            var merged = fromDeps
                .Concat(fromModules)
                .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return merged.Count == 0 ? file : WithIncludedMods(file, merged);
        }).ToList();
    }

    private async Task<Dictionary<string, ModpackContentItem>> FetchCurseForgeProjectSummariesAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, ModpackContentItem>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return map;

        try
        {
            var numeric = ids
                .Select(id => int.TryParse(id, out var n) ? n : 0)
                .Where(n => n > 0)
                .Distinct()
                .ToList();
            if (numeric.Count == 0)
                return map;

            for (var offset = 0; offset < numeric.Count; offset += 50)
            {
                var batch = numeric.Skip(offset).Take(50).ToList();
                using var content = new StringContent(
                    JsonSerializer.Serialize(new { modIds = batch }),
                    Encoding.UTF8,
                    "application/json");
                using var response = await _http
                    .PostAsync($"{CurseForgeApiBase}/mods", content, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var project in data.EnumerateArray())
                {
                    var id = project.TryGetProperty("id", out var idEl) ? idEl.GetInt64().ToString() : null;
                    var name = project.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                        continue;

                    string? iconUrl = null;
                    if (project.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object)
                    {
                        if (logo.TryGetProperty("thumbnailUrl", out var thumb))
                            iconUrl = thumb.GetString();
                        if (string.IsNullOrWhiteSpace(iconUrl) && logo.TryGetProperty("url", out var urlEl))
                            iconUrl = urlEl.GetString();
                    }

                    var slug = project.TryGetProperty("slug", out var slugEl) ? slugEl.GetString() : null;
                    string? web = null;
                    if (project.TryGetProperty("links", out var links) &&
                        links.ValueKind == JsonValueKind.Object &&
                        links.TryGetProperty("websiteUrl", out var webEl))
                    {
                        web = webEl.GetString();
                    }

                    if (string.IsNullOrWhiteSpace(web) && !string.IsNullOrWhiteSpace(slug))
                        web = $"https://www.curseforge.com/minecraft/mc-mods/{slug.Trim()}";

                    map[id] = CreateContentItem(
                        id.Trim(),
                        ModSearchViewModel.SourceIdCurseForge,
                        name.Trim(),
                        iconUrl,
                        web,
                        slug);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort — module filenames remain as fallback.
        }

        return map;
    }

    private static ModpackContentItem CreateContentItem(
        string id,
        string sourceId,
        string title,
        string? iconUrl,
        string? webPageUrl,
        string? slug)
    {
        Uri? iconUri = null;
        if (!string.IsNullOrWhiteSpace(iconUrl) &&
            Uri.TryCreate(iconUrl.Trim(), UriKind.Absolute, out var remote) &&
            (remote.Scheme == Uri.UriSchemeHttp || remote.Scheme == Uri.UriSchemeHttps))
        {
            iconUri = remote;
        }

        return new ModpackContentItem
        {
            Id = id,
            SourceId = sourceId,
            Title = title,
            IconUrl = iconUrl,
            IconUri = iconUri,
            WebPageUrl = webPageUrl,
            Slug = slug
        };
    }

    private static ModpackContentItem CreateFallbackContent(
        string id,
        string sourceId,
        string title,
        bool canOpenInLauncher = true) =>
        new()
        {
            Id = canOpenInLauncher ? id : string.Empty,
            SourceId = sourceId,
            Title = title
        };

    private static ModFileVersionItem WithIncludedMods(
        ModFileVersionItem file,
        IReadOnlyList<ModpackContentItem> included)
    {
        if (included.Count == 0)
            return file;

        var names = included
            .Select(c => c.Title)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        const int previewCount = 4;
        var preview = names.Count <= previewCount
            ? string.Join(" · ", names)
            : string.Join(" · ", names.Take(previewCount)) + "…";
        var tooltip = string.Join(Environment.NewLine, names);

        return new ModFileVersionItem
        {
            Id = file.Id,
            DisplayName = file.DisplayName,
            FileName = file.FileName,
            DownloadUrl = file.DownloadUrl,
            Channel = file.Channel,
            GameVersions = file.GameVersions,
            Loaders = file.Loaders,
            Published = file.Published,
            Dependencies = file.Dependencies,
            Sha1 = file.Sha1,
            GameVersionsLabel = file.GameVersionsLabel,
            LoadersLabel = file.LoadersLabel,
            IncludedContents = included,
            IncludedMods = names,
            IncludedModsLabel = preview,
            IncludedModsTooltip = tooltip,
            IncludedModsCountLabel = Loc.Format(LocKeys.Modpack_ContentsCount, included.Count)
        };
    }

    private static ModFileVersionItem WithIncludedMods(
        ModFileVersionItem file,
        IReadOnlyList<string> includedNames)
    {
        if (includedNames.Count == 0)
            return file;

        var contents = includedNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => CreateFallbackContent(
                "name:" + n,
                ModSearchViewModel.SourceIdCurseForge,
                n,
                canOpenInLauncher: false))
            .ToList();
        return WithIncludedMods(file, contents);
    }

    private static IReadOnlyList<string> ParseCurseForgeModuleNames(JsonElement file)
    {
        if (!file.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
            return [];

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules.EnumerateArray())
        {
            if (!module.TryGetProperty("name", out var nameEl))
                continue;
            var raw = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var cleaned = Path.GetFileNameWithoutExtension(raw.Trim());
            if (string.IsNullOrWhiteSpace(cleaned) || !seen.Add(cleaned))
                continue;
            names.Add(cleaned);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static string? BuildForgeCdnUrl(long fileId, string fileName)
    {
        if (fileId <= 0 || string.IsNullOrWhiteSpace(fileName))
            return null;

        var a = fileId / 1000;
        var b = fileId % 1000;
        return $"https://edge.forgecdn.net/files/{a}/{b}/{Uri.EscapeDataString(fileName)}";
    }
}
