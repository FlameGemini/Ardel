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
/// Searches remote Mod catalogs. Modrinth is queried directly; CurseForge uses the public site API.
/// </summary>
public sealed class ModCatalogService
{
    private const int PageSize = 40;

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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var hits = new List<ModProjectItem>();
        string? warning = null;
        Exception? modrinthError = null;
        Exception? curseForgeError = null;

        var includeModrinth = criteria.SourceId is ModSearchViewModel.SourceIdAll or ModSearchViewModel.SourceIdModrinth;
        var includeCurseForge = criteria.SourceId is ModSearchViewModel.SourceIdAll or ModSearchViewModel.SourceIdCurseForge;

        Task<List<ModProjectItem>>? modrinthTask = null;
        Task<List<ModProjectItem>>? curseForgeTask = null;

        if (includeModrinth)
            modrinthTask = SearchModrinthAsync(criteria, cancellationToken);
        if (includeCurseForge)
            curseForgeTask = SearchCurseForgeAsync(criteria, cancellationToken);

        if (modrinthTask is not null)
        {
            try
            {
                hits.AddRange(await modrinthTask.ConfigureAwait(false));
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
                hits.AddRange(await curseForgeTask.ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                curseForgeError = ex;
            }
        }

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

            return new ModCatalogSearchResult([], null);
        }

        if (modrinthError is not null)
            warning = Loc.Format(LocKeys.Mod_SearchPartialModrinth, modrinthError.Message);
        else if (curseForgeError is not null)
            warning = Loc.Format(LocKeys.Mod_SearchPartialCurseForge, curseForgeError.Message);

        // Stable ordering: higher downloads first within this page.
        hits = hits
            .OrderByDescending(h => h.Downloads)
            .ThenBy(h => h.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModCatalogSearchResult(hits, warning);
    }

    private async Task<List<ModProjectItem>> SearchModrinthAsync(
        ModSearchCriteria criteria,
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

            list.Add(new ModProjectItem
            {
                Id = hit.ProjectId,
                SourceId = ModSearchViewModel.SourceIdModrinth,
                Title = hit.Title.Trim(),
                Description = (hit.Description ?? string.Empty).Trim(),
                SourceLabel = sourceLabel,
                IconUrl = hit.IconUrl,
                Downloads = hit.Downloads,
                DownloadsLabel = FormatDownloads(hit.Downloads)
            });
        }

        return list;
    }

    private async Task<List<ModProjectItem>> SearchCurseForgeAsync(
        ModSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        // Website JSON API used elsewhere in Ardel (no official CF console key required).
        var url = new StringBuilder("https://www.curseforge.com/api/v1/mods/search")
            .Append("?gameId=432&classId=6&pageSize=").Append(PageSize)
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
            if (entry.TryGetProperty("logo", out var logo) &&
                logo.ValueKind == JsonValueKind.Object &&
                logo.TryGetProperty("thumbnailUrl", out var thumb))
            {
                icon = thumb.GetString();
            }

            list.Add(new ModProjectItem
            {
                Id = id,
                SourceId = ModSearchViewModel.SourceIdCurseForge,
                Title = title.Trim(),
                Description = summary.Trim(),
                SourceLabel = sourceLabel,
                IconUrl = icon,
                Downloads = downloads,
                DownloadsLabel = FormatDownloads(downloads)
            });
        }

        return list;
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
    }
}

public sealed record ModCatalogSearchResult(
    IReadOnlyList<ModProjectItem> Items,
    string? WarningMessage);
