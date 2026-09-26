using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ardel.Launcher.Services.SkinRelay;

/// <summary>
/// Caches the open-source authlib-injector agent under Ardel's runtime folder.
/// Versioned file names avoid stomping unrelated jars.
/// </summary>
internal static class AuthlibAgentCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string CatalogOfficial =
        "https://authlib-injector.yushi.moe/artifact/latest.json";
    private const string CatalogMirror =
        "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json";

    public static async Task<string> ResolveAgentJarAsync(
        HttpClient http,
        bool preferMirror,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            "runtime",
            "agents");
        Directory.CreateDirectory(root);

        var existing = Directory.EnumerateFiles(root, "authlib-injector-*.jar")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault(f => new FileInfo(f).Length > 10_000);
        if (existing is not null)
            return existing;

        var catalog = await TryFetchCatalogAsync(http, preferMirror, cancellationToken)
            .ConfigureAwait(false);
        catalog ??= await TryFetchCatalogAsync(http, preferMirror: false, cancellationToken)
            .ConfigureAwait(false);

        if (catalog is null)
        {
            var fallback = Directory.EnumerateFiles(root, "authlib-injector-*.jar")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (fallback is not null)
                return fallback;
            throw new InvalidOperationException("Authlib agent catalog unavailable.");
        }

        var build = catalog.BuildNumber?.ToString() ?? catalog.Version ?? "latest";
        var safeBuild = string.Concat(build.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
        var jarPath = Path.Combine(root, $"authlib-injector-{safeBuild}.jar");
        var stampPath = jarPath + ".sha256";

        if (File.Exists(jarPath) && new FileInfo(jarPath).Length > 10_000)
        {
            if (string.IsNullOrWhiteSpace(catalog.Checksums?.Sha256))
                return jarPath;

            string? known = null;
            if (File.Exists(stampPath))
                known = (await File.ReadAllTextAsync(stampPath, cancellationToken).ConfigureAwait(false)).Trim();
            known ??= await HashFileAsync(jarPath, cancellationToken).ConfigureAwait(false);

            if (string.Equals(known, catalog.Checksums.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(stampPath))
                    await File.WriteAllTextAsync(stampPath, known, cancellationToken).ConfigureAwait(false);
                return jarPath;
            }
        }

        var url = catalog.DownloadUrl
                  ?? throw new InvalidOperationException("Authlib agent download URL missing.");
        if (preferMirror)
            url = RewriteToMirror(url);

        var temp = jarPath + ".partial";
        await using (var input = await http.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(temp))
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(catalog.Checksums?.Sha256))
        {
            var actual = await HashFileAsync(temp, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, catalog.Checksums.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(temp); } catch { /* ignore */ }
                throw new InvalidOperationException("Authlib agent integrity check failed.");
            }

            await File.WriteAllTextAsync(stampPath, catalog.Checksums.Sha256, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Copy(temp, jarPath, overwrite: true);
        try { File.Delete(temp); } catch { /* ignore */ }

        // Keep only the newest couple of agents.
        foreach (var old in Directory.EnumerateFiles(root, "authlib-injector-*.jar")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(3))
        {
            try { File.Delete(old); } catch { /* ignore */ }
            try { File.Delete(old + ".sha256"); } catch { /* ignore */ }
        }

        return jarPath;
    }

    private static async Task<CatalogEntry?> TryFetchCatalogAsync(
        HttpClient http,
        bool preferMirror,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = preferMirror ? CatalogMirror : CatalogOfficial;
            await using var stream = await http.GetStreamAsync(url, cancellationToken)
                .ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<CatalogEntry>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinRelay] Catalog fetch failed: {ex.Message}");
            return null;
        }
    }

    private static string RewriteToMirror(string url) =>
        url.Replace(
            "https://authlib-injector.yushi.moe/",
            "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/",
            StringComparison.OrdinalIgnoreCase);

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class CatalogEntry
    {
        [JsonPropertyName("build_number")]
        public int? BuildNumber { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("checksums")]
        public Checksums? Checksums { get; set; }
    }

    private sealed class Checksums
    {
        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }
}
