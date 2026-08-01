using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Services;

/// <summary>
/// Downloads a catalog modpack archive, creates an isolated instance with the
/// required loader, then pulls remote files and applies overrides.
/// </summary>
public sealed class ModpackInstallService : IDisposable
{
    private const int MaxParallelDownloads = 4;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly IMinecraftLaunchService _launchService;

    public ModpackInstallService(IMinecraftLaunchService launchService)
    {
        _launchService = launchService;
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8
        })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Ardel-Launcher/1.0 (Modpack install)");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void Dispose() => _http.Dispose();

    public async Task InstallAsync(
        LauncherSettings settings,
        ModpackInstallRequest request,
        IProgress<FileProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(request);

        var instanceName = request.InstanceName.Trim();
        var nameError = NameRules.ValidateVersionName(instanceName, GamePaths.GetVersionsRoot(settings.GameDirectory));
        if (nameError is not null)
            throw new InvalidOperationException(nameError);

        var tempRoot = Path.Combine(Path.GetTempPath(), "Ardel", "modpacks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var archivePath = Path.Combine(tempRoot, "pack.bin");

        try
        {
            progress?.Report(new FileProgressInfo(Loc.Get(LocKeys.Modpack_DownloadingPack), 0, 0));
            await DownloadToFileAsync(request.PackDownloadUrl, archivePath, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new FileProgressInfo(Loc.Get(LocKeys.Modpack_Parsing), 0, 0));
            var plan = await ParseArchiveAsync(archivePath, request.SourceId, cancellationToken)
                .ConfigureAwait(false);

            if (plan.Loader == ModLoaderKind.None && string.IsNullOrWhiteSpace(plan.MinecraftVersion))
                throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_InvalidManifest));

            if (plan.Loader is not (ModLoaderKind.None or ModLoaderKind.Fabric or ModLoaderKind.Forge
                or ModLoaderKind.NeoForge))
            {
                throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_UnsupportedLoader));
            }

            progress?.Report(new FileProgressInfo(
                Loc.Format(LocKeys.Modpack_InstallingLoader, plan.MinecraftVersion), 0, 0));

            var installRequest = new InstallRequest
            {
                MinecraftVersionId = plan.MinecraftVersion,
                CustomVersionName = instanceName,
                Loader = plan.Loader,
                LoaderVersion = plan.LoaderVersion,
                InstallFabricApi = false
            };

            await _launchService.InstallAsync(
                    settings,
                    installRequest,
                    progress,
                    byteProgress: null,
                    cancellationToken)
                .ConfigureAwait(false);

            var instanceDir = GamePaths.GetVersionInstanceDirectory(instanceName, settings.GameDirectory);
            GamePaths.EnsureVersionIsolation(instanceName, settings.GameDirectory);

            var files = plan.RemoteFiles;
            var total = files.Count;
            var done = 0;
            using var gate = new SemaphoreSlim(MaxParallelDownloads);
            var errors = new List<string>();

            await Task.WhenAll(files.Select(async file =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = ResolveSafePath(instanceDir, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await DownloadWithFallbackAsync(file, target, cancellationToken).ConfigureAwait(false);
                    var finished = Interlocked.Increment(ref done);
                    progress?.Report(new FileProgressInfo(
                        Path.GetFileName(file.RelativePath),
                        finished,
                        Math.Max(1, total)));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lock (errors)
                        errors.Add($"{file.RelativePath}: {ex.Message}");
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(false);

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    Loc.Format(LocKeys.Modpack_FileDownloadFailed, errors.Count, errors[0]));
            }

            progress?.Report(new FileProgressInfo(Loc.Get(LocKeys.Modpack_ApplyingOverrides), 0, 0));
            ApplyOverrides(plan, instanceDir);

            GamePaths.MarkAsUserInstance(instanceName, settings.GameDirectory);
            GamePaths.MarkLaunchReady(instanceName, settings.GameDirectory);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // best-effort temp cleanup
            }
        }
    }

    private async Task<ModpackPlan> ParseArchiveAsync(
        string archivePath,
        string sourceId,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(archivePath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var mrIndex = zip.GetEntry("modrinth.index.json");
        if (mrIndex is not null)
            return await ParseMrpackAsync(zip, mrIndex, archivePath, cancellationToken).ConfigureAwait(false);

        var manifest = zip.GetEntry("manifest.json");
        if (manifest is not null)
            return await ParseCurseForgeAsync(zip, manifest, archivePath, cancellationToken).ConfigureAwait(false);

        // Some CurseForge exports nest manifest one level down.
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.Count(c => c is '/' or '\\') <= 1)
            {
                return await ParseCurseForgeAsync(zip, entry, archivePath, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (string.Equals(sourceId, ModSearchViewModel.SourceIdModrinth, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_MissingMrpackIndex));

        throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_MissingManifest));
    }

    private static async Task<ModpackPlan> ParseMrpackAsync(
        ZipArchive zip,
        ZipArchiveEntry indexEntry,
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var entryStream = indexEntry.Open();
        using var doc = await JsonDocument.ParseAsync(entryStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "Modpack" : "Modpack";
        var deps = root.TryGetProperty("dependencies", out var depsEl) && depsEl.ValueKind == JsonValueKind.Object
            ? depsEl
            : default;

        var minecraft = ReadDep(deps, "minecraft")
                        ?? throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_MissingMinecraft));

        var loader = ModLoaderKind.None;
        string? loaderVersion = null;
        if (ReadDep(deps, "fabric-loader") is { Length: > 0 } fabric)
        {
            loader = ModLoaderKind.Fabric;
            loaderVersion = fabric;
        }
        else if (ReadDep(deps, "forge") is { Length: > 0 } forge)
        {
            loader = ModLoaderKind.Forge;
            loaderVersion = forge;
        }
        else if (ReadDep(deps, "neoforge") is { Length: > 0 } neo)
        {
            loader = ModLoaderKind.NeoForge;
            loaderVersion = neo;
        }
        else if (ReadDep(deps, "quilt-loader") is not null)
        {
            throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_UnsupportedLoader));
        }

        var remote = new List<ModpackRemoteFile>();
        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in files.EnumerateArray())
            {
                if (!ShouldInstallClientFile(file))
                    continue;

                var path = file.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var urls = new List<string>();
                if (file.TryGetProperty("downloads", out var dl) && dl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in dl.EnumerateArray())
                    {
                        if (u.GetString() is { Length: > 0 } url)
                            urls.Add(url);
                    }
                }

                if (urls.Count == 0)
                    continue;

                string? sha1 = null;
                if (file.TryGetProperty("hashes", out var hashes) &&
                    hashes.ValueKind == JsonValueKind.Object &&
                    hashes.TryGetProperty("sha1", out var shaEl))
                {
                    sha1 = shaEl.GetString();
                }

                long? size = null;
                if (file.TryGetProperty("fileSize", out var sizeEl) && sizeEl.TryGetInt64(out var sz))
                    size = sz;

                remote.Add(new ModpackRemoteFile
                {
                    RelativePath = path.Replace('\\', '/').TrimStart('/'),
                    DownloadUrls = urls,
                    FileSize = size,
                    Sha1 = sha1
                });
            }
        }

        _ = zip; // overrides read later from archive path
        return new ModpackPlan
        {
            Name = name,
            MinecraftVersion = minecraft,
            Loader = loader,
            LoaderVersion = loaderVersion,
            RemoteFiles = remote,
            ArchivePath = archivePath,
            ArchiveKind = ModpackArchiveKind.Mrpack
        };
    }

    private async Task<ModpackPlan> ParseCurseForgeAsync(
        ZipArchive zip,
        ZipArchiveEntry manifestEntry,
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var entryStream = manifestEntry.Open();
        using var doc = await JsonDocument.ParseAsync(entryStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "Modpack" : "Modpack";
        if (!root.TryGetProperty("minecraft", out var mc) || mc.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_InvalidManifest));

        var minecraft = mc.TryGetProperty("version", out var verEl)
            ? verEl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(minecraft))
            throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_MissingMinecraft));

        var loader = ModLoaderKind.None;
        string? loaderVersion = null;
        if (mc.TryGetProperty("modLoaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in loaders.EnumerateArray())
            {
                var id = entry.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (TryParseCurseLoader(id, out loader, out loaderVersion))
                    break;
            }
        }

        if (loader == ModLoaderKind.None)
            throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_UnsupportedLoader));

        var remote = new List<ModpackRemoteFile>();
        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in files.EnumerateArray())
            {
                var required = !file.TryGetProperty("required", out var reqEl) ||
                               reqEl.ValueKind != JsonValueKind.False;
                if (!required)
                    continue;

                var projectId = file.TryGetProperty("projectID", out var pEl) ? pEl.GetInt32() :
                    file.TryGetProperty("projectId", out var p2) ? p2.GetInt32() : 0;
                var fileId = file.TryGetProperty("fileID", out var fEl) ? fEl.GetInt32() :
                    file.TryGetProperty("fileId", out var f2) ? f2.GetInt32() : 0;
                if (projectId <= 0 || fileId <= 0)
                    continue;

                var resolved = await ResolveCurseForgeFileAsync(projectId, fileId, cancellationToken)
                    .ConfigureAwait(false);
                if (resolved is null)
                    continue;

                remote.Add(resolved);
            }
        }

        _ = zip;
        return new ModpackPlan
        {
            Name = name,
            MinecraftVersion = minecraft,
            Loader = loader,
            LoaderVersion = loaderVersion,
            RemoteFiles = remote,
            ArchivePath = archivePath,
            ArchiveKind = ModpackArchiveKind.CurseForgeZip
        };
    }

    private async Task<ModpackRemoteFile?> ResolveCurseForgeFileAsync(
        int projectId,
        int fileId,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.curse.tools/v1/mods/{projectId}/files/{fileId}";
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return null;

        var fileName = data.TryGetProperty("fileName", out var fn) ? fn.GetString() : null;
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"{projectId}-{fileId}.jar";

        var downloadUrl = data.TryGetProperty("downloadUrl", out var dl) ? dl.GetString() : null;
        if (string.IsNullOrWhiteSpace(downloadUrl))
            downloadUrl = $"https://api.curse.tools/v1/mods/{projectId}/files/{fileId}/download";

        return new ModpackRemoteFile
        {
            RelativePath = "mods/" + fileName.Trim(),
            DownloadUrls = [downloadUrl]
        };
    }

    private static bool TryParseCurseLoader(string id, out ModLoaderKind loader, out string? version)
    {
        loader = ModLoaderKind.None;
        version = null;
        var dash = id.IndexOf('-');
        if (dash <= 0 || dash >= id.Length - 1)
            return false;

        var kind = id[..dash];
        version = id[(dash + 1)..];
        if (kind.Equals("forge", StringComparison.OrdinalIgnoreCase))
        {
            loader = ModLoaderKind.Forge;
            return true;
        }

        if (kind.Equals("neoforge", StringComparison.OrdinalIgnoreCase))
        {
            loader = ModLoaderKind.NeoForge;
            return true;
        }

        if (kind.Equals("fabric", StringComparison.OrdinalIgnoreCase))
        {
            loader = ModLoaderKind.Fabric;
            return true;
        }

        return false;
    }

    private static string? ReadDep(JsonElement deps, string key)
    {
        if (deps.ValueKind != JsonValueKind.Object)
            return null;
        return deps.TryGetProperty(key, out var el) ? el.GetString() : null;
    }

    private static bool ShouldInstallClientFile(JsonElement file)
    {
        if (!file.TryGetProperty("env", out var env) || env.ValueKind != JsonValueKind.Object)
            return true;

        if (!env.TryGetProperty("client", out var client))
            return true;

        var value = client.GetString();
        return !string.Equals(value, "unsupported", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyOverrides(ModpackPlan plan, string instanceDir)
    {
        using var stream = File.OpenRead(plan.ArchivePath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        if (plan.ArchiveKind == ModpackArchiveKind.Mrpack)
        {
            CopyZipFolder(zip, "overrides/", instanceDir);
            CopyZipFolder(zip, "client-overrides/", instanceDir);
            return;
        }

        // CurseForge: manifest may name overrides folder; default "overrides".
        CopyZipFolder(zip, "overrides/", instanceDir);
    }

    private static void CopyZipFolder(ZipArchive zip, string prefix, string instanceDir)
    {
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.EndsWith('/'))
                continue;

            var relative = name[prefix.Length..];
            if (string.IsNullOrWhiteSpace(relative))
                continue;

            var target = ResolveSafePath(instanceDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string ResolveSafePath(string instanceDir, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) ||
            normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(Loc.Format(LocKeys.Modpack_UnsafePath, relativePath));
        }

        var full = Path.GetFullPath(Path.Combine(instanceDir, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(instanceDir);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Loc.Format(LocKeys.Modpack_UnsafePath, relativePath));
        }

        return full;
    }

    private async Task DownloadWithFallbackAsync(
        ModpackRemoteFile file,
        string targetPath,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var url in file.DownloadUrls)
        {
            try
            {
                await DownloadToFileAsync(url, targetPath, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(file.Sha1) && File.Exists(targetPath))
                {
                    var actual = await ComputeSha1Async(targetPath, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(actual, file.Sha1, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_HashMismatch));
                }

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                TryDelete(targetPath);
            }
        }

        throw last ?? new InvalidOperationException(Loc.Get(LocKeys.Modpack_FileDownloadFailedGeneric));
    }

    private async Task DownloadToFileAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tmp = targetPath + ".part";
        TryDelete(tmp);
        TryDelete(targetPath);

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken)
                         .ConfigureAwait(false))
        await using (var output = new FileStream(
                         tmp,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         82_000,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tmp, targetPath, overwrite: true);
    }

    private static async Task<string> ComputeSha1Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Suggest a unique instance folder name from a pack title.</summary>
    public static string SuggestInstanceName(string? packTitle, string? versionsRoot)
    {
        var raw = string.IsNullOrWhiteSpace(packTitle) ? "Modpack" : packTitle.Trim();
        Span<char> buffer = stackalloc char[Math.Min(raw.Length, 80)];
        var n = 0;
        foreach (var ch in raw)
        {
            if (n >= buffer.Length)
                break;
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')
                buffer[n++] = ch;
            else if (char.IsWhiteSpace(ch) && n > 0 && buffer[n - 1] != '-')
                buffer[n++] = '-';
        }

        var baseName = n == 0 ? "Modpack" : new string(buffer[..n]).Trim('-', '.', '_');
        if (baseName.Length == 0)
            baseName = "Modpack";

        if (NameRules.ValidateVersionName(baseName, versionsRoot) is null)
            return baseName;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{baseName}-{i}";
            if (NameRules.ValidateVersionName(candidate, versionsRoot) is null)
                return candidate;
        }

        return baseName + "-" + Guid.NewGuid().ToString("N")[..6];
    }
}
