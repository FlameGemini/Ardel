using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Services;

/// <summary>
/// Resolves and downloads Fabric API into a version's <c>mods/</c> folder.
/// Prefers Modrinth; falls back to CurseForge website API.
/// </summary>
public sealed class FabricApiModService
{
    public const string ModrinthSlug = "fabric-api";
    public const int CurseForgeProjectId = 306612;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public FabricApiModService(HttpClient http)
    {
        _http = http;
    }

    public async Task InstallAsync(
        string minecraftVersionId,
        string modsDirectory,
        IProgress<string>? status,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modsDirectory);
        Directory.CreateDirectory(modsDirectory);

        status?.Report(Loc.Get(LocKeys.FabricApi_Resolving));

        ModFileHit? hit = null;
        Exception? modrinthError = null;

        try
        {
            hit = await ResolveFromModrinthAsync(minecraftVersionId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            modrinthError = ex;
        }

        if (hit is null)
        {
            try
            {
                hit = await ResolveFromCurseForgeAsync(minecraftVersionId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var detail = modrinthError is null
                    ? ex.Message
                    : Loc.Format(LocKeys.FabricApi_BothFailed, modrinthError.Message, ex.Message);
                throw new InvalidOperationException(
                    Loc.Format(LocKeys.FabricApi_ResolveFailed, minecraftVersionId, detail),
                    ex);
            }
        }

        if (hit is null)
        {
            throw new InvalidOperationException(
                Loc.Format(LocKeys.FabricApi_NotFound, minecraftVersionId));
        }

        status?.Report(Loc.Format(LocKeys.FabricApi_Downloading, hit.FileName, hit.Source));

        var targetPath = Path.Combine(modsDirectory, hit.FileName);
        await DownloadFileAsync(hit.DownloadUrl, targetPath, cancellationToken).ConfigureAwait(false);

        status?.Report(Loc.Format(LocKeys.FabricApi_Installed, hit.FileName, hit.Source));
    }

    private async Task<ModFileHit?> ResolveFromModrinthAsync(
        string minecraftVersionId,
        CancellationToken cancellationToken)
    {
        var loaders = Uri.EscapeDataString("""["fabric"]""");
        var games = Uri.EscapeDataString($"[\"{minecraftVersionId}\"]");
        var url =
            $"https://api.modrinth.com/v2/project/{ModrinthSlug}/version?loaders={loaders}&game_versions={games}&limit=20";

        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var versions = await response.Content
            .ReadFromJsonAsync<List<ModrinthVersion>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (versions is null || versions.Count == 0)
            return null;

        var preferred = versions
            .OrderByDescending(v => string.Equals(v.VersionType, "release", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.DatePublished)
            .FirstOrDefault();

        var file = preferred?.Files?.FirstOrDefault(f => f.Primary)
                   ?? preferred?.Files?.FirstOrDefault();

        if (preferred is null || file is null || string.IsNullOrWhiteSpace(file.Url))
            return null;

        return new ModFileHit(
            file.Filename ?? $"fabric-api-{preferred.VersionNumber}.jar",
            file.Url,
            Loc.Get(LocKeys.FabricApi_SourceModrinth));
    }

    private async Task<ModFileHit?> ResolveFromCurseForgeAsync(
        string minecraftVersionId,
        CancellationToken cancellationToken)
    {
        // Official CF Core API shape via community proxy (www.curseforge.com/api rejects anonymous clients).
        // Use /v1/... (not /v1/cf/...), which avoids an HTTPS→HTTP 302 that HttpClient will not follow.
        var url =
            $"https://api.curse.tools/v1/mods/{CurseForgeProjectId}/files" +
            $"?pageSize=50&index=0" +
            $"&gameVersion={Uri.EscapeDataString(minecraftVersionId)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var file in data.EnumerateArray())
        {
            if (!IsCurseForgeFabricFile(file, minecraftVersionId))
                continue;

            var fileId = file.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
            var fileName = file.TryGetProperty("fileName", out var nameEl)
                ? nameEl.GetString()
                : null;

            if (fileId <= 0 || string.IsNullOrWhiteSpace(fileName))
                continue;

            var downloadUrl = file.TryGetProperty("downloadUrl", out var dlEl)
                ? dlEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                downloadUrl =
                    $"https://api.curse.tools/v1/mods/{CurseForgeProjectId}/files/{fileId}/download";
            }

            return new ModFileHit(
                fileName,
                downloadUrl,
                Loc.Get(LocKeys.FabricApi_SourceCurseForge));
        }

        return null;
    }

    private static bool IsCurseForgeFabricFile(JsonElement file, string minecraftVersionId)
    {
        if (file.TryGetProperty("gameVersions", out var versions) &&
            versions.ValueKind == JsonValueKind.Array)
        {
            var hasMc = false;
            var hasFabric = false;
            foreach (var v in versions.EnumerateArray())
            {
                var s = v.GetString();
                if (string.IsNullOrEmpty(s))
                    continue;
                if (string.Equals(s, minecraftVersionId, StringComparison.OrdinalIgnoreCase))
                    hasMc = true;
                if (s.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                    hasFabric = true;
            }

            if (hasMc && hasFabric)
                return true;
        }

        return false;
    }

    private async Task DownloadFileAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

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
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(targetPath))
                File.Delete(targetPath);
            File.Move(temp, targetPath);
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

    private sealed record ModFileHit(string FileName, string DownloadUrl, string Source);

    private sealed class ModrinthVersion
    {
        [JsonPropertyName("version_number")]
        public string? VersionNumber { get; set; }

        [JsonPropertyName("version_type")]
        public string? VersionType { get; set; }

        [JsonPropertyName("date_published")]
        public DateTimeOffset DatePublished { get; set; }

        [JsonPropertyName("files")]
        public List<ModrinthFile>? Files { get; set; }
    }

    private sealed class ModrinthFile
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("primary")]
        public bool Primary { get; set; }
    }
}
