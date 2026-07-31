using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Services;

/// <summary>
/// Searches remote Mod catalogs. Modrinth is queried directly;
/// CurseForge uses the Core API via a community proxy (site API returns 403 without a key).
/// </summary>
public sealed partial class ModCatalogService
{
    public const int PageSize = 40;

    /// <summary>CurseForge Core API shape via community proxy (site /api/v1 rejects unauthenticated clients).</summary>
    private const string CurseForgeApiBase = "https://api.curse.tools/v1";

    /// <summary>
    /// Soft weight so Modrinth remains visible when merged with CurseForge's larger raw download totals.
    /// </summary>
    private const double ModrinthDownloadWeight = 4.0;

    private static readonly HashSet<string> ModrinthLoaderSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "forge", "neoforge", "fabric", "quilt", "liteloader"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public ModCatalogService()
    {
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Ardel-Launcher/1.0 (Mod catalog)");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ModCatalogSearchResult> SearchAsync(
        ModSearchCriteria criteria,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (offset < 0)
            offset = 0;

        string? warning = null;
        Exception? modrinthError = null;
        Exception? curseForgeError = null;
        var anyFullPage = false;

        var includeModrinth = criteria.SourceId is ModSearchViewModel.SourceIdAll or ModSearchViewModel.SourceIdModrinth;
        var includeCurseForge = criteria.SourceId is ModSearchViewModel.SourceIdAll or ModSearchViewModel.SourceIdCurseForge;

        List<ModProjectItem> modrinthHits = [];
        List<ModProjectItem> curseForgeHits = [];

        Task<List<ModProjectItem>>? modrinthTask = null;
        Task<List<ModProjectItem>>? curseForgeTask = null;

        if (includeModrinth)
            modrinthTask = SearchModrinthAsync(criteria, offset, cancellationToken);
        if (includeCurseForge)
            curseForgeTask = SearchCurseForgeAsync(criteria, offset, cancellationToken);

        if (modrinthTask is not null)
        {
            try
            {
                modrinthHits = await modrinthTask.ConfigureAwait(false);
                if (modrinthHits.Count >= PageSize)
                    anyFullPage = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                modrinthError = ex;
            }
        }

        if (curseForgeTask is not null)
        {
            try
            {
                curseForgeHits = await curseForgeTask.ConfigureAwait(false);
                if (curseForgeHits.Count >= PageSize)
                    anyFullPage = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                curseForgeError = ex;
            }
        }

        var hasKeyword = !string.IsNullOrWhiteSpace(criteria.Keyword);
        List<ModProjectItem> hits;
        if (includeModrinth && includeCurseForge)
            hits = MergeCatalogPages(modrinthHits, curseForgeHits, hasKeyword);
        else if (includeModrinth)
            hits = modrinthHits; // Keep Modrinth API order (relevance).
        else
            hits = curseForgeHits; // Keep CurseForge API order (popularity).

        if (hits.Count == 0)
        {
            if (modrinthError is not null && curseForgeError is not null)
            {
                throw new InvalidOperationException(
                    Loc.Format(LocKeys.Mod_SearchBothFailed, modrinthError.Message, curseForgeError.Message));
            }

            if (modrinthError is not null)
                throw new InvalidOperationException(Loc.Format(LocKeys.Mod_SearchFailed, modrinthError.Message), modrinthError);

            if (curseForgeError is not null)
                throw new InvalidOperationException(Loc.Format(LocKeys.Mod_SearchFailed, curseForgeError.Message), curseForgeError);

            return new ModCatalogSearchResult([], null, false, offset);
        }

        if (modrinthError is not null)
            warning = Loc.Format(LocKeys.Mod_SearchPartialModrinth, modrinthError.Message);
        else if (curseForgeError is not null)
            warning = Loc.Format(LocKeys.Mod_SearchPartialCurseForge, curseForgeError.Message);

        return new ModCatalogSearchResult(hits, warning, anyFullPage, offset + PageSize);
    }

    /// <summary>
    /// Merge dual-source pages: drop near-duplicate projects (prefer Modrinth),
    /// then rank with keyword relevance + weighted downloads so CurseForge raw counts
    /// do not bury Modrinth hits.
    /// </summary>
    private static List<ModProjectItem> MergeCatalogPages(
        IReadOnlyList<ModProjectItem> modrinth,
        IReadOnlyList<ModProjectItem> curseForge,
        bool hasKeyword)
    {
        var slots = new List<MergeSlot>(modrinth.Count + curseForge.Count);
        var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        void Consider(ModProjectItem item, int sourceIndex, bool isModrinth)
        {
            var key = ProjectMergeKey(item);
            if (key.Length == 0)
            {
                slots.Add(new MergeSlot(item, sourceIndex, isModrinth));
                return;
            }

            if (!indexByKey.TryGetValue(key, out var existing))
            {
                indexByKey[key] = slots.Count;
                slots.Add(new MergeSlot(item, sourceIndex, isModrinth));
                return;
            }

            // Same project on both catalogs → keep Modrinth.
            if (!slots[existing].IsModrinth && isModrinth)
                slots[existing] = new MergeSlot(item, sourceIndex, isModrinth);
        }

        for (var i = 0; i < modrinth.Count; i++)
            Consider(modrinth[i], i, isModrinth: true);
        for (var i = 0; i < curseForge.Count; i++)
            Consider(curseForge[i], i, isModrinth: false);

        return slots
            .OrderByDescending(s => RankMergedHit(s, hasKeyword))
            .ThenBy(s => s.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Item)
            .ToList();
    }

    private static double RankMergedHit(MergeSlot slot, bool hasKeyword)
    {
        // CurseForge download totals are often much larger; weight Modrinth so "All"
        // still surfaces relevant Modrinth projects.
        var downloads = Math.Log10(1d + Math.Max(0, slot.Item.Downloads));
        var weightedDownloads = downloads * (slot.IsModrinth ? ModrinthDownloadWeight : 1d);

        if (!hasKeyword)
            return weightedDownloads;

        // Modrinth search is relevance-ordered; earlier ranks should stay ahead.
        var relevance = Math.Max(0, 400 - slot.SourceIndex);
        return relevance + weightedDownloads;
    }

    private static string ProjectMergeKey(ModProjectItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
            return string.Empty;

        Span<char> buffer = stackalloc char[item.Title.Length];
        var n = 0;
        foreach (var ch in item.Title)
        {
            if (char.IsLetterOrDigit(ch))
                buffer[n++] = char.ToLowerInvariant(ch);
        }

        return n == 0 ? string.Empty : new string(buffer[..n]);
    }

    private readonly record struct MergeSlot(ModProjectItem Item, int SourceIndex, bool IsModrinth);

    private async Task<List<ModProjectItem>> SearchModrinthAsync(
        ModSearchCriteria criteria,
        int offset,
        CancellationToken cancellationToken)
    {
        var facets = new List<string> { """["project_type:mod"]""" };

        var category = CategorySlug(criteria.CategoryId);
        if (!string.IsNullOrEmpty(category))
            facets.Add($"""["categories:'{EscapeFacet(category)}'"]""");

        var loader = ModrinthLoader(criteria.LoaderId);
        if (!string.IsNullOrEmpty(loader))
            facets.Add($"""["categories:'{EscapeFacet(loader)}'"]""");

        if (!string.IsNullOrWhiteSpace(criteria.GameVersion))
            facets.Add($"""["versions:'{EscapeFacet(criteria.GameVersion)}'"]""");

        var url = new StringBuilder("https://api.modrinth.com/v2/search?limit=")
            .Append(PageSize)
            .Append("&offset=")
            .Append(offset)
            .Append("&index=relevance");

        if (!string.IsNullOrWhiteSpace(criteria.Keyword))
            url.Append("&query=").Append(Uri.EscapeDataString(criteria.Keyword));

        url.Append("&facets=[").Append(string.Join(',', facets)).Append(']');

        using var response = await _http.GetAsync(url.ToString(), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<ModrinthSearchResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var sourceLabel = Loc.Get(LocKeys.Mod_SourceModrinth);
        var list = new List<ModProjectItem>();
        foreach (var hit in payload?.Hits ?? [])
        {
            if (string.IsNullOrWhiteSpace(hit.ProjectId) || string.IsNullOrWhiteSpace(hit.Title))
                continue;

            var loaders = (hit.Categories ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c) && ModrinthLoaderSlugs.Contains(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var versions = hit.Versions ?? [];
            if (!MatchesGameVersion(versions, criteria.GameVersion))
                continue;

            list.Add(CreateItem(
                hit.ProjectId,
                ModSearchViewModel.SourceIdModrinth,
                hit.Title.Trim(),
                (hit.Description ?? string.Empty).Trim(),
                sourceLabel,
                hit.IconUrl,
                hit.Downloads,
                versions,
                loaders,
                criteria.GameVersion));
        }

        return list;
    }

    private async Task<List<ModProjectItem>> SearchCurseForgeAsync(
        ModSearchCriteria criteria,
        int offset,
        CancellationToken cancellationToken)
    {
        var url = new StringBuilder(CurseForgeApiBase)
            .Append("/mods/search?gameId=432&classId=6&pageSize=").Append(PageSize)
            .Append("&index=").Append(offset)
            .Append("&sortField=2&sortOrder=desc");

        var categoryId = CategoryCurseForgeId(criteria.CategoryId);
        if (categoryId > 0)
            url.Append("&categoryId=").Append(categoryId);

        var loaderType = CurseForgeLoaderType(criteria.LoaderId);
        if (loaderType > 0)
            url.Append("&modLoaderType=").Append(loaderType);

        if (!string.IsNullOrWhiteSpace(criteria.GameVersion))
            url.Append("&gameVersion=").Append(Uri.EscapeDataString(criteria.GameVersion));

        if (!string.IsNullOrWhiteSpace(criteria.Keyword))
            url.Append("&searchFilter=").Append(Uri.EscapeDataString(criteria.Keyword));

        using var response = await _http.GetAsync(url.ToString(), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var sourceLabel = Loc.Get(LocKeys.Mod_SourceCurseForge);
        var list = new List<ModProjectItem>();
        foreach (var entry in data.EnumerateArray())
        {
            var id = entry.TryGetProperty("id", out var idEl) ? idEl.GetInt32().ToString() : null;
            var title = entry.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                continue;

            var summary = entry.TryGetProperty("summary", out var sumEl) ? sumEl.GetString() ?? string.Empty : string.Empty;
            long downloads = 0;
            if (entry.TryGetProperty("downloadCount", out var dlEl) && dlEl.TryGetInt64(out var dl))
                downloads = dl;

            string? icon = null;
            if (entry.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object)
            {
                if (logo.TryGetProperty("thumbnailUrl", out var thumb))
                    icon = thumb.GetString();
                if (string.IsNullOrWhiteSpace(icon) && logo.TryGetProperty("url", out var urlEl))
                    icon = urlEl.GetString();
            }

            var versions = new List<string>();
            var loaders = new List<string>();
            if (entry.TryGetProperty("latestFilesIndexes", out var indexes) && indexes.ValueKind == JsonValueKind.Array)
            {
                foreach (var idx in indexes.EnumerateArray())
                {
                    if (idx.TryGetProperty("gameVersion", out var gv) && gv.GetString() is { Length: > 0 } version)
                        versions.Add(version);

                    if (idx.TryGetProperty("modLoader", out var ml) && ml.TryGetInt32(out var loaderCode))
                    {
                        var slug = CurseForgeLoaderSlug(loaderCode);
                        if (slug is not null)
                            loaders.Add(slug);
                    }
                }
            }

            if (!MatchesGameVersion(versions, criteria.GameVersion))
                continue;

            list.Add(CreateItem(
                id,
                ModSearchViewModel.SourceIdCurseForge,
                title.Trim(),
                summary.Trim(),
                sourceLabel,
                icon,
                downloads,
                versions,
                loaders,
                criteria.GameVersion));
        }

        return list;
    }

    public async Task<ModProjectDetail> GetProjectDetailAsync(
        ModProjectItem project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        return project.SourceId switch
        {
            ModSearchViewModel.SourceIdModrinth =>
                await GetModrinthDetailAsync(project, cancellationToken).ConfigureAwait(false),
            ModSearchViewModel.SourceIdCurseForge =>
                await GetCurseForgeDetailAsync(project, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(Loc.Format(LocKeys.Mod_DetailUnknownSource, project.SourceId))
        };
    }

    public async Task DownloadFileAsync(
        string url,
        string targetPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        using var response = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        var temp = targetPath + ".tmp";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken)
                             .ConfigureAwait(false))
            await using (var output = new FileStream(
                             temp,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    written += read;
                    if (total is > 0)
                        progress?.Report(Math.Clamp(100d * written / total.Value, 0, 99.5));
                }
            }

            if (File.Exists(targetPath))
                File.Delete(targetPath);
            File.Move(temp, targetPath);
            progress?.Report(100);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch { /* ignore */ }
            }
        }
    }

    private async Task<ModProjectDetail> GetModrinthDetailAsync(
        ModProjectItem project,
        CancellationToken cancellationToken)
    {
        using var projectResponse = await _http
            .GetAsync($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(project.Id)}", cancellationToken)
            .ConfigureAwait(false);
        projectResponse.EnsureSuccessStatusCode();

        await using var projectStream = await projectResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var projectDoc = await JsonDocument.ParseAsync(projectStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = projectDoc.RootElement;

        var title = root.TryGetProperty("title", out var titleEl)
            ? titleEl.GetString()?.Trim() ?? project.Title
            : project.Title;
        var description = root.TryGetProperty("description", out var descEl)
            ? descEl.GetString()?.Trim() ?? project.Description
            : project.Description;
        var slug = root.TryGetProperty("slug", out var slugEl) ? slugEl.GetString() : null;
        var iconUrl = root.TryGetProperty("icon_url", out var iconEl) ? iconEl.GetString() : project.IconUrl;

        var loaders = new List<string>();
        if (root.TryGetProperty("loaders", out var loadersEl) && loadersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in loadersEl.EnumerateArray())
            {
                if (l.GetString() is { Length: > 0 } s)
                    loaders.Add(s);
            }
        }

        var gameVersions = new List<string>();
        if (root.TryGetProperty("game_versions", out var gvEl) && gvEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in gvEl.EnumerateArray())
            {
                if (v.GetString() is { Length: > 0 } s)
                    gameVersions.Add(s);
            }
        }

        var projectUrl = !string.IsNullOrWhiteSpace(slug)
            ? $"https://modrinth.com/mod/{slug}"
            : $"https://modrinth.com/mod/{project.Id}";

        using var versionsResponse = await _http
            .GetAsync(
                $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(project.Id)}/version",
                cancellationToken)
            .ConfigureAwait(false);
        versionsResponse.EnsureSuccessStatusCode();

        var versionPayload = await versionsResponse.Content
            .ReadFromJsonAsync<List<ModrinthVersionDto>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var files = new List<ModFileVersionItem>();
        foreach (var ver in versionPayload ?? [])
        {
            var file = ver.Files?.FirstOrDefault(f => f.Primary) ?? ver.Files?.FirstOrDefault();
            if (file is null || string.IsNullOrWhiteSpace(file.Url))
                continue;

            var fileLoaders = ver.Loaders ?? [];
            files.Add(new ModFileVersionItem
            {
                Id = ver.Id ?? Guid.NewGuid().ToString("N"),
                DisplayName = string.IsNullOrWhiteSpace(ver.VersionNumber)
                    ? (ver.Name ?? file.Filename ?? "mod")
                    : ver.VersionNumber.Trim(),
                FileName = string.IsNullOrWhiteSpace(file.Filename)
                    ? $"{title}.jar"
                    : file.Filename.Trim(),
                DownloadUrl = file.Url,
                Channel = ParseModrinthChannel(ver.VersionType),
                GameVersions = ver.GameVersions ?? [],
                Loaders = fileLoaders,
                LoadersLabel = FormatLoaders(fileLoaders),
                Published = ver.DatePublished,
                Dependencies = ParseModrinthDependencies(ver.Dependencies)
            });
        }

        return BuildDetail(
            project.Id,
            ModSearchViewModel.SourceIdModrinth,
            title,
            description,
            Loc.Get(LocKeys.Mod_SourceModrinth),
            projectUrl,
            iconUrl,
            gameVersions,
            loaders,
            files);
    }

    private async Task<ModProjectDetail> GetCurseForgeDetailAsync(
        ModProjectItem project,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(project.Id, out var modId))
            throw new InvalidOperationException(Loc.Format(LocKeys.Mod_DetailInvalidId, project.Id));

        using var projectResponse = await _http
            .GetAsync($"{CurseForgeApiBase}/mods/{modId}", cancellationToken)
            .ConfigureAwait(false);
        projectResponse.EnsureSuccessStatusCode();

        await using var projectStream = await projectResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var projectDoc = await JsonDocument.ParseAsync(projectStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!projectDoc.RootElement.TryGetProperty("data", out var data))
            throw new InvalidOperationException(Loc.Get(LocKeys.Mod_DetailLoadFailed));

        var title = data.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()?.Trim() ?? project.Title
            : project.Title;
        var description = data.TryGetProperty("summary", out var sumEl)
            ? sumEl.GetString()?.Trim() ?? project.Description
            : project.Description;

        string? iconUrl = project.IconUrl;
        if (data.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object)
        {
            if (logo.TryGetProperty("thumbnailUrl", out var thumb))
                iconUrl = thumb.GetString() ?? iconUrl;
            if (string.IsNullOrWhiteSpace(iconUrl) && logo.TryGetProperty("url", out var urlEl))
                iconUrl = urlEl.GetString();
        }

        var projectUrl = $"https://www.curseforge.com/minecraft/mc-mods/{project.Id}";
        if (data.TryGetProperty("links", out var links) &&
            links.ValueKind == JsonValueKind.Object &&
            links.TryGetProperty("websiteUrl", out var web) &&
            web.GetString() is { Length: > 0 } website)
        {
            projectUrl = website;
        }
        else if (data.TryGetProperty("slug", out var slugEl) &&
                 slugEl.GetString() is { Length: > 0 } slug)
        {
            projectUrl = $"https://www.curseforge.com/minecraft/mc-mods/{slug}";
        }

        var files = new List<ModFileVersionItem>();
        var index = 0;
        while (index < 150 && files.Count < 120)
        {
            var pageUrl =
                $"{CurseForgeApiBase}/mods/{modId}/files?pageSize=50&index={index}";
            using var filesResponse = await _http.GetAsync(pageUrl, cancellationToken).ConfigureAwait(false);
            filesResponse.EnsureSuccessStatusCode();

            await using var filesStream = await filesResponse.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var filesDoc = await JsonDocument.ParseAsync(filesStream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!filesDoc.RootElement.TryGetProperty("data", out var page) ||
                page.ValueKind != JsonValueKind.Array)
                break;

            var pageCount = 0;
            foreach (var file in page.EnumerateArray())
            {
                pageCount++;
                var fileId = file.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
                var fileName = file.TryGetProperty("fileName", out var fnEl) ? fnEl.GetString() : null;
                if (fileId <= 0 || string.IsNullOrWhiteSpace(fileName))
                    continue;

                var downloadUrl = file.TryGetProperty("downloadUrl", out var dlEl)
                    ? dlEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                    downloadUrl = $"{CurseForgeApiBase}/mods/{modId}/files/{fileId}/download";

                var displayName = file.TryGetProperty("displayName", out var dnEl)
                    ? dnEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = fileName;

                var releaseType = file.TryGetProperty("releaseType", out var rtEl) && rtEl.TryGetInt32(out var rt)
                    ? rt
                    : 1;

                var gameVersions = new List<string>();
                var loaders = new List<string>();
                if (file.TryGetProperty("gameVersions", out var gv) && gv.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in gv.EnumerateArray())
                    {
                        var s = entry.GetString();
                        if (string.IsNullOrWhiteSpace(s))
                            continue;
                        var trimmed = s.Trim();
                        var loaderSlug = GuessLoaderSlug(trimmed);
                        if (loaderSlug is not null)
                            loaders.Add(loaderSlug);
                        else
                            gameVersions.Add(trimmed);
                    }
                }

                DateTimeOffset? published = null;
                if (file.TryGetProperty("fileDate", out var dateEl) &&
                    dateEl.GetString() is { Length: > 0 } dateText &&
                    DateTimeOffset.TryParse(dateText, out var parsed))
                {
                    published = parsed;
                }

                files.Add(new ModFileVersionItem
                {
                    Id = fileId.ToString(),
                    DisplayName = displayName.Trim(),
                    FileName = fileName.Trim(),
                    DownloadUrl = downloadUrl,
                    Channel = ParseCurseForgeChannel(releaseType),
                    GameVersions = gameVersions,
                    Loaders = loaders,
                    LoadersLabel = FormatLoaders(loaders),
                    Published = published,
                    Dependencies = ParseCurseForgeDependencies(file)
                });
            }

            if (pageCount < 50)
                break;
            index += pageCount;
        }

        var allGame = files.SelectMany(f => f.GameVersions).Distinct(StringComparer.OrdinalIgnoreCase);
        var allLoaders = files.SelectMany(f => f.Loaders).Distinct(StringComparer.OrdinalIgnoreCase);

        return BuildDetail(
            project.Id,
            ModSearchViewModel.SourceIdCurseForge,
            title,
            description,
            Loc.Get(LocKeys.Mod_SourceCurseForge),
            projectUrl,
            iconUrl,
            allGame,
            allLoaders,
            files);
    }

    private static ModProjectDetail BuildDetail(
        string id,
        string sourceId,
        string title,
        string description,
        string sourceLabel,
        string projectUrl,
        string? iconUrl,
        IEnumerable<string> versions,
        IEnumerable<string> loaders,
        IReadOnlyList<ModFileVersionItem> files)
    {
        Uri? iconUri = null;
        if (!string.IsNullOrWhiteSpace(iconUrl) &&
            Uri.TryCreate(iconUrl.Trim(), UriKind.Absolute, out var remote) &&
            (remote.Scheme == Uri.UriSchemeHttp || remote.Scheme == Uri.UriSchemeHttps))
        {
            iconUri = remote;
        }

        return new ModProjectDetail
        {
            Id = id,
            SourceId = sourceId,
            Title = title,
            Description = description,
            SourceLabel = sourceLabel,
            ProjectUrl = projectUrl,
            IconUrl = iconUrl,
            IconUri = iconUri,
            VersionsLabel = FormatVersions(versions, preferredGameVersion: null),
            LoadersLabel = FormatLoaders(loaders),
            Files = files
        };
    }

    private static ModReleaseChannel ParseModrinthChannel(string? type) =>
        type?.Trim().ToLowerInvariant() switch
        {
            "beta" => ModReleaseChannel.Beta,
            "alpha" => ModReleaseChannel.Alpha,
            _ => ModReleaseChannel.Release
        };

    private static ModReleaseChannel ParseCurseForgeChannel(int releaseType) => releaseType switch
    {
        2 => ModReleaseChannel.Beta,
        3 => ModReleaseChannel.Alpha,
        _ => ModReleaseChannel.Release
    };

    private static string? GuessLoaderSlug(string token)
    {
        var t = token.Trim().ToLowerInvariant();
        return t switch
        {
            "forge" => "forge",
            "neoforge" => "neoforge",
            "fabric" => "fabric",
            "quilt" => "quilt",
            "liteloader" => "liteloader",
            _ => null
        };
    }

    private static ModProjectItem CreateItem(
        string id,
        string sourceId,
        string title,
        string description,
        string sourceLabel,
        string? iconUrl,
        long downloads,
        IEnumerable<string> versions,
        IEnumerable<string> loaders,
        string? preferredGameVersion = null)
    {
        Uri? iconUri = null;
        if (!string.IsNullOrWhiteSpace(iconUrl) &&
            Uri.TryCreate(iconUrl.Trim(), UriKind.Absolute, out var remote) &&
            (remote.Scheme == Uri.UriSchemeHttp || remote.Scheme == Uri.UriSchemeHttps))
        {
            iconUri = remote;
        }

        return new ModProjectItem
        {
            Id = id,
            SourceId = sourceId,
            Title = title,
            Description = description,
            SourceLabel = sourceLabel,
            IconUrl = iconUrl,
            IconUri = iconUri,
            Downloads = downloads,
            DownloadsLabel = FormatDownloads(downloads),
            VersionsLabel = FormatVersions(versions, preferredGameVersion),
            LoadersLabel = FormatLoaders(loaders)
        };
    }

    private static bool MatchesGameVersion(IEnumerable<string> versions, string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
            return true;

        foreach (var version in versions)
        {
            if (!string.IsNullOrWhiteSpace(version) &&
                string.Equals(version.Trim(), gameVersion.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string FormatVersions(IEnumerable<string> versions, string? preferredGameVersion = null)
    {
        var list = versions
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(preferredGameVersion))
        {
            var prefer = preferredGameVersion.Trim();
            list = list
                .OrderByDescending(v => string.Equals(v, prefer, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var joined = string.Join(", ", list.Take(4));
        return list.Count > 4
            ? Loc.Format(LocKeys.Mod_VersionsEllipsis, joined)
            : joined;
    }

    private static string FormatLoaders(IEnumerable<string> loaders)
    {
        var labels = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in loaders)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var slug = raw.Trim().ToLowerInvariant();
            if (!seen.Add(slug))
                continue;

            var label = slug switch
            {
                "forge" => Loc.Get(LocKeys.Mod_LoaderForge),
                "neoforge" => Loc.Get(LocKeys.Mod_LoaderNeoForge),
                "fabric" => Loc.Get(LocKeys.Mod_LoaderFabric),
                "quilt" => Loc.Get(LocKeys.Mod_LoaderQuilt),
                "liteloader" => Loc.Get(LocKeys.Mod_LoaderLiteLoader),
                _ => null
            };
            if (label is not null)
                labels.Add(label);
        }

        return labels.Count == 0 ? string.Empty : string.Join(" · ", labels);
    }

    private static string? CategorySlug(string categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            return null;
        var slash = categoryId.IndexOf('/');
        if (slash < 0 || slash >= categoryId.Length - 1)
            return null;
        var slug = categoryId[(slash + 1)..].Trim();
        return string.IsNullOrEmpty(slug) ? null : slug;
    }

    private static int CategoryCurseForgeId(string categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            return 0;
        var slash = categoryId.IndexOf('/');
        var left = slash >= 0 ? categoryId[..slash] : categoryId;
        return int.TryParse(left, out var id) ? id : 0;
    }

    private static string? ModrinthLoader(string loaderId) => loaderId switch
    {
        "1" => "forge",
        "16" => "neoforge",
        "4" => "fabric",
        "8" => "quilt",
        "2" => "liteloader",
        _ => null
    };

    private static int CurseForgeLoaderType(string loaderId) => loaderId switch
    {
        "1" => 1,
        "2" => 3,
        "4" => 4,
        "8" => 5,
        "16" => 6,
        _ => 0
    };

    private static string? CurseForgeLoaderSlug(int code) => code switch
    {
        1 => "forge",
        3 => "liteloader",
        4 => "fabric",
        5 => "quilt",
        6 => "neoforge",
        _ => null
    };

    private static string EscapeFacet(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static string FormatDownloads(long count)
    {
        if (count >= 1_000_000)
            return Loc.Format(LocKeys.Mod_DownloadsMillions, (count / 1_000_000d).ToString("0.#"));
        if (count >= 1_000)
            return Loc.Format(LocKeys.Mod_DownloadsThousands, (count / 1_000d).ToString("0.#"));
        return Loc.Format(LocKeys.Mod_DownloadsExact, count);
    }

    private sealed class ModrinthSearchResponse
    {
        public List<ModrinthHit>? Hits { get; set; }
    }

    private sealed class ModrinthHit
    {
        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; }

        public string? Title { get; set; }
        public string? Description { get; set; }

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        public long Downloads { get; set; }

        public List<string>? Versions { get; set; }
        public List<string>? Categories { get; set; }
    }

    private sealed class ModrinthVersionDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }

        [JsonPropertyName("version_number")]
        public string? VersionNumber { get; set; }

        [JsonPropertyName("version_type")]
        public string? VersionType { get; set; }

        [JsonPropertyName("date_published")]
        public DateTimeOffset? DatePublished { get; set; }

        [JsonPropertyName("game_versions")]
        public List<string>? GameVersions { get; set; }

        public List<string>? Loaders { get; set; }
        public List<ModrinthFileDto>? Files { get; set; }
        public List<ModrinthDependencyDto>? Dependencies { get; set; }
    }

    private sealed class ModrinthDependencyDto
    {
        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; }

        [JsonPropertyName("version_id")]
        public string? VersionId { get; set; }

        [JsonPropertyName("dependency_type")]
        public string? DependencyType { get; set; }
    }

    private sealed class ModrinthFileDto
    {
        public string? Url { get; set; }
        public string? Filename { get; set; }
        public bool Primary { get; set; }
    }
}

public sealed record ModCatalogSearchResult(
    IReadOnlyList<ModProjectItem> Items,
    string? WarningMessage,
    bool HasMore,
    int NextOffset);
