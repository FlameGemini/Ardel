using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>
/// Exports an isolated instance as a Modrinth <c>.mrpack</c>
/// (optional thin <c>files[]</c> + overrides).
/// </summary>
public static class ModpackExportService
{
    private static readonly string[] OptionFiles =
    [
        "options.txt",
        "optionsof.txt",
        "servers.dat"
    ];

    /// <summary>Write a shareable <c>.mrpack</c> for <paramref name="versionId"/>.</summary>
    public static Task ExportMrpackAsync(
        string versionId,
        string mrpackPath,
        ModpackExportOptions? options = null,
        string? minecraftRoot = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default) =>
        ExportMrpackAsync(
            versionId,
            mrpackPath,
            options ?? new ModpackExportOptions(),
            catalog: null,
            minecraftRoot,
            status,
            cancellationToken);

    public static async Task ExportMrpackAsync(
        string versionId,
        string mrpackPath,
        ModpackExportOptions options,
        ModCatalogService? catalog,
        string? minecraftRoot = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mrpackPath);
        ArgumentNullException.ThrowIfNull(options);

        var id = versionId.Trim();
        var instanceDir = GamePaths.GetVersionInstanceDirectory(id, minecraftRoot);
        if (!Directory.Exists(instanceDir))
            throw new DirectoryNotFoundException(Loc.Format(LocKeys.Error_VersionFolderNotFound, id));

        var minecraft = GamePaths.ResolveBaseGameVersion(id, minecraftRoot);
        if (string.IsNullOrWhiteSpace(minecraft))
            minecraft = VersionKindDetector.DetectBaseGameVersion(id, minecraftRoot);
        if (string.IsNullOrWhiteSpace(minecraft))
            throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_MissingMinecraft));

        var jsonPath = Path.Combine(instanceDir, id + ".json");
        var kind = VersionKind.Vanilla;
        string? loaderVersion = null;
        if (File.Exists(jsonPath))
        {
            var json = File.ReadAllText(jsonPath);
            kind = VersionKindDetector.DetectFromJson(json);
            if (kind == VersionKind.Vanilla)
                kind = VersionKindDetector.DetectFromId(id);
            loaderVersion = TryReadLoaderVersion(json, kind);
        }
        else
        {
            kind = VersionKindDetector.Detect(id, minecraftRoot);
        }

        if (kind is VersionKind.OptiFine or VersionKind.Custom)
            throw new InvalidOperationException(Loc.Get(LocKeys.Modpack_ExportUnsupportedLoader));

        var destDir = Path.GetDirectoryName(mrpackPath);
        if (!string.IsNullOrWhiteSpace(destDir))
            Directory.CreateDirectory(destDir);

        if (File.Exists(mrpackPath))
            File.Delete(mrpackPath);

        var remoteFiles = new List<(string RelativePath, ModrinthVersionFileHit Hit)>();
        var skipEmbed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options.PreferThinPack && options.IncludeMods)
        {
            status?.Report(Loc.Get(LocKeys.Modpack_ExportResolvingThin));
            catalog ??= new ModCatalogService();
            var modsDir = Path.Combine(instanceDir, "mods");
            if (Directory.Exists(modsDir))
            {
                await ResolveThinModsAsync(
                        modsDir,
                        catalog,
                        remoteFiles,
                        skipEmbed,
                        status,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        status?.Report(Loc.Get(LocKeys.InstanceSettings_Exporting));
        var indexJson = BuildIndexJson(id, minecraft, kind, loaderVersion, remoteFiles);

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var zip = ZipFile.Open(mrpackPath, ZipArchiveMode.Create);
            var indexEntry = zip.CreateEntry("modrinth.index.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(indexEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                writer.Write(indexJson);

            void AddFolderIf(bool include, string folder)
            {
                if (!include)
                    return;
                var source = Path.Combine(instanceDir, folder);
                if (!Directory.Exists(source))
                    return;
                AddDirectory(zip, source, "overrides/" + folder, skipEmbed, folder);
            }

            AddFolderIf(options.IncludeMods, "mods");
            AddFolderIf(options.IncludeConfig, "config");
            AddFolderIf(options.IncludeResourcePacks, "resourcepacks");
            AddFolderIf(options.IncludeShaderPacks, "shaderpacks");
            AddFolderIf(options.IncludeDatapacks, "datapacks");
            AddFolderIf(options.IncludeSaves, "saves");
            AddFolderIf(options.IncludeScreenshots, "screenshots");

            if (options.IncludeOptions)
            {
                foreach (var fileName in OptionFiles)
                {
                    var source = Path.Combine(instanceDir, fileName);
                    if (File.Exists(source))
                        zip.CreateEntryFromFile(source, "overrides/" + fileName, CompressionLevel.Optimal);
                }
            }

            foreach (var icon in Directory.EnumerateFiles(instanceDir, "ardel-icon.*"))
            {
                var name = Path.GetFileName(icon);
                if (string.IsNullOrEmpty(name))
                    continue;
                zip.CreateEntryFromFile(icon, "overrides/" + name, CompressionLevel.Optimal);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ResolveThinModsAsync(
        string modsDir,
        ModCatalogService catalog,
        List<(string RelativePath, ModrinthVersionFileHit Hit)> remoteFiles,
        HashSet<string> skipEmbed,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var jars = Directory.EnumerateFiles(modsDir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    return false; // keep disabled mods fully local
                var ext = Path.GetExtension(f);
                return ext.Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".zip", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        using var gate = new SemaphoreSlim(4);
        var lockObj = new object();
        var done = 0;
        var total = jars.Count;

        await Task.WhenAll(jars.Select(async path =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sha1 = await ComputeSha1Async(path, cancellationToken).ConfigureAwait(false);
                var hit = await catalog.FindVersionFileBySha1Async(sha1, cancellationToken).ConfigureAwait(false);
                var fileName = Path.GetFileName(path);
                var relative = "mods/" + fileName;
                if (hit is not null)
                {
                    lock (lockObj)
                    {
                        remoteFiles.Add((relative, hit));
                        skipEmbed.Add(relative);
                    }
                }

                var n = Interlocked.Increment(ref done);
                status?.Report(Loc.Format(LocKeys.Modpack_ExportThinProgress, n, total));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // leave file in overrides
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);
    }

    private static async Task<string> ComputeSha1Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildIndexJson(
        string packName,
        string minecraft,
        VersionKind kind,
        string? loaderVersion,
        IReadOnlyList<(string RelativePath, ModrinthVersionFileHit Hit)> remoteFiles)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", 1);
            writer.WriteString("game", "minecraft");
            writer.WriteString("versionId", "ardel-export");
            writer.WriteString("name", packName);
            writer.WriteString("summary", "Exported from Ardel");

            writer.WriteStartArray("files");
            foreach (var (relativePath, hit) in remoteFiles)
            {
                writer.WriteStartObject();
                writer.WriteString("path", relativePath.Replace('\\', '/'));
                writer.WriteStartObject("hashes");
                writer.WriteString("sha1", hit.Sha1);
                if (!string.IsNullOrWhiteSpace(hit.Sha512))
                    writer.WriteString("sha512", hit.Sha512);
                writer.WriteEndObject();
                writer.WriteStartArray("downloads");
                writer.WriteStringValue(hit.DownloadUrl);
                writer.WriteEndArray();
                if (hit.FileSize is { } size)
                    writer.WriteNumber("fileSize", size);
                writer.WriteStartObject("env");
                writer.WriteString("client", "required");
                writer.WriteString("server", "unsupported");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartObject("dependencies");
            writer.WriteString("minecraft", minecraft);
            switch (kind)
            {
                case VersionKind.Fabric when !string.IsNullOrWhiteSpace(loaderVersion):
                    writer.WriteString("fabric-loader", loaderVersion);
                    break;
                case VersionKind.Fabric:
                    writer.WriteString("fabric-loader", "*");
                    break;
                case VersionKind.Quilt when !string.IsNullOrWhiteSpace(loaderVersion):
                    writer.WriteString("quilt-loader", loaderVersion);
                    break;
                case VersionKind.Quilt:
                    writer.WriteString("quilt-loader", "*");
                    break;
                case VersionKind.Forge when !string.IsNullOrWhiteSpace(loaderVersion):
                    writer.WriteString("forge", NormalizeForgeVersion(minecraft, loaderVersion));
                    break;
                case VersionKind.Forge:
                    writer.WriteString("forge", "*");
                    break;
                case VersionKind.NeoForge when !string.IsNullOrWhiteSpace(loaderVersion):
                    writer.WriteString("neoforge", loaderVersion);
                    break;
                case VersionKind.NeoForge:
                    writer.WriteString("neoforge", "*");
                    break;
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string NormalizeForgeVersion(string minecraft, string loaderVersion)
    {
        var v = loaderVersion.Trim();
        var prefix = minecraft.Trim() + "-";
        if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return v[prefix.Length..];
        return v;
    }

    private static string? TryReadLoaderVersion(string json, VersionKind kind)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
            {
                foreach (var lib in libs.EnumerateArray())
                {
                    if (!lib.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                        continue;
                    var name = nameEl.GetString() ?? string.Empty;
                    var ver = ExtractMavenVersion(name);
                    if (ver is null)
                        continue;

                    if (kind == VersionKind.Fabric &&
                        (name.Contains(":fabric-loader:", StringComparison.OrdinalIgnoreCase) ||
                         name.StartsWith("net.fabricmc:fabric-loader:", StringComparison.OrdinalIgnoreCase)))
                        return ver;

                    if (kind == VersionKind.Quilt &&
                        (name.Contains(":quilt-loader:", StringComparison.OrdinalIgnoreCase) ||
                         name.StartsWith("org.quiltmc:quilt-loader:", StringComparison.OrdinalIgnoreCase)))
                        return ver;

                    if (kind == VersionKind.Forge &&
                        name.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase))
                        return ver;

                    if (kind == VersionKind.NeoForge &&
                        (name.StartsWith("net.neoforged:neoforge:", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains(":neoforge:", StringComparison.OrdinalIgnoreCase)))
                        return ver;
                }
            }

            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            {
                var id = idEl.GetString() ?? string.Empty;
                if (kind == VersionKind.Fabric)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        id, @"fabric-loader-([0-9][^/\\]*)-1\.\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                        return m.Groups[1].Value;
                }

                if (kind == VersionKind.Forge)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        id, @"forge-([0-9][\w.\-]+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                        return m.Groups[1].Value;
                }

                if (kind == VersionKind.NeoForge)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        id, @"neoforge-([0-9][\w.\-]+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                        return m.Groups[1].Value;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? ExtractMavenVersion(string mavenName)
    {
        var parts = mavenName.Split(':');
        return parts.Length >= 3 ? parts[^1].Trim() : null;
    }

    private static void AddDirectory(
        ZipArchive zip,
        string sourceDir,
        string zipPrefix,
        HashSet<string> skipEmbed,
        string topFolder)
    {
        var prefix = zipPrefix.Replace('\\', '/').TrimEnd('/') + "/";
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativeUnderFolder = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relativeUnderFolder) ||
                relativeUnderFolder.Contains("..", StringComparison.Ordinal))
                continue;

            var packRelative = topFolder + "/" + relativeUnderFolder;
            if (skipEmbed.Contains(packRelative))
                continue;

            zip.CreateEntryFromFile(file, prefix + relativeUnderFolder, CompressionLevel.Optimal);
        }
    }
}
