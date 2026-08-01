using System.Diagnostics;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Services;

/// <summary>
/// Portable game paths: <c>{launcherRoot}/.minecraft</c> with forced per-version isolation.
/// </summary>
public static class GamePaths
{
    public const string TrashPrefix = "_trash_";

    /// <summary>Marks a versions/ folder as a loader dependency (hidden from instance list).</summary>
    public const string DependencyMarker = ".ardel-dependency";

    /// <summary>Marks a versions/ folder as a user-facing instance (always listed).</summary>
    public const string UserInstanceMarker = ".ardel-user";

    /// <summary>Written after a successful file install. Ready launches skip full SHA1 verify.</summary>
    public const string ReadyMarker = ".ardel-ready";

    /// <summary>
    /// Launcher root for <c>.minecraft</c>.
    /// Published: next to the exe. Dev (<c>dotnet run</c> / Debug): repo root (folder with Ardel.slnx).
    /// </summary>
    public static string GetLauncherDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = AppDomain.CurrentDomain.BaseDirectory;

        var exeDir = Path.GetFullPath(baseDir);
        var repoRoot = FindRepoRoot(exeDir);
        return repoRoot ?? exeDir;
    }

    /// <summary><c>{launcherRoot}/.minecraft</c></summary>
    public static string GetMinecraftRoot()
    {
        var root = Path.Combine(GetLauncherDirectory(), ".minecraft");
        Directory.CreateDirectory(root);
        return root;
    }

    public static string GetVersionsRoot(string? minecraftRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(minecraftRoot) ? GetMinecraftRoot() : minecraftRoot;
        var versions = Path.Combine(root, "versions");
        Directory.CreateDirectory(versions);
        return versions;
    }

    /// <summary>
    /// Isolated instance dir for a version (mods / saves / config live here).
    /// <c>{launcherRoot}/.minecraft/versions/{versionId}</c>
    /// </summary>
    public static string GetVersionInstanceDirectory(string versionId, string? minecraftRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        var root = string.IsNullOrWhiteSpace(minecraftRoot) ? GetMinecraftRoot() : minecraftRoot;
        var dir = Path.Combine(root, "versions", versionId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Create isolation subfolders under the version instance.</summary>
    public static string EnsureVersionIsolation(string versionId, string? minecraftRoot = null)
    {
        var dir = GetVersionInstanceDirectory(versionId, minecraftRoot);

        string[] isolatedFolders =
        [
            "mods",
            "saves",
            "config",
            "resourcepacks",
            "datapacks",
            "shaderpacks",
            "modpacks",
            "screenshots",
            "logs"
        ];

        foreach (var name in isolatedFolders)
            Directory.CreateDirectory(Path.Combine(dir, name));

        return dir;
    }

    /// <summary>True when the version profile is usable (json present; jar optional for loaders).</summary>
    public static bool IsVersionFullyInstalled(string versionId, string? minecraftRoot = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return false;

        var dir = Path.Combine(GetVersionsRoot(minecraftRoot), versionId);
        var jsonPath = Path.Combine(dir, versionId + ".json");
        if (!File.Exists(jsonPath))
            return false;

        // Vanilla clients ship a matching jar; Fabric/Forge/NeoForge profiles often inherit
        // and only provide json. Treat a valid profile json as installed either way.
        var jarPath = Path.Combine(dir, versionId + ".jar");
        return File.Exists(jarPath) || IsVersionProfileJson(jsonPath);
    }

    /// <summary>
    /// Cheap local sanity check (profile + marker + parent chain).
    /// When true, launch skips full InstallAsync SHA1 verification.
    /// </summary>
    public static bool IsLaunchReady(string versionId, string? minecraftRoot = null)
    {
        if (!IsVersionFullyInstalled(versionId, minecraftRoot))
            return false;

        // Require an explicit post-install marker so half-written profiles don't skip repair.
        if (!HasMarker(versionId, ReadyMarker, minecraftRoot))
            return false;

        var root = string.IsNullOrWhiteSpace(minecraftRoot) ? GetMinecraftRoot() : minecraftRoot;

        // Walk inheritsFrom so Forge/Fabric parents are present.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { versionId };
        var current = versionId;
        while (true)
        {
            var parent = TryGetInheritsFrom(current, root);
            if (parent is null)
                break;
            if (!visited.Add(parent))
                break;
            if (!IsVersionFullyInstalled(parent, root))
                return false;
            current = parent;
        }

        var libraries = Path.Combine(root, "libraries");
        if (!Directory.Exists(libraries) ||
            !Directory.EnumerateFileSystemEntries(libraries).Any())
            return false;

        var indexes = Path.Combine(root, "assets", "indexes");
        if (!Directory.Exists(indexes) ||
            !Directory.EnumerateFiles(indexes, "*.json").Any())
            return false;

        return true;
    }

    public static void MarkLaunchReady(string versionId, string? minecraftRoot = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return;

        try
        {
            var dir = Path.Combine(GetVersionsRoot(minecraftRoot), versionId);
            if (!Directory.Exists(dir))
                return;
            File.WriteAllText(Path.Combine(dir, ReadyMarker), string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamePaths] MarkLaunchReady failed: {ex.Message}");
        }
    }

    /// <summary>
    /// One-shot migrate for installs created before <see cref="ReadyMarker"/> existed.
    /// Only marks folders that already look fully installed.
    /// </summary>
    public static void MigrateLaunchReadyMarkers(string? minecraftRoot = null)
    {
        try
        {
            foreach (var id in ListVersionFolderNames(minecraftRoot))
            {
                if (!IsVersionFullyInstalled(id, minecraftRoot))
                    continue;
                if (HasMarker(id, ReadyMarker, minecraftRoot))
                    continue;
                MarkLaunchReady(id, minecraftRoot);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamePaths] MigrateLaunchReadyMarkers failed: {ex.Message}");
        }
    }

    /// <summary>Reads <c>inheritsFrom</c> from a version JSON, or null.</summary>
    public static string? TryGetInheritsFrom(string versionId, string? minecraftRoot = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return null;

        try
        {
            var jsonPath = Path.Combine(GetVersionsRoot(minecraftRoot), versionId, versionId + ".json");
            if (!File.Exists(jsonPath))
                return null;

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (doc.RootElement.TryGetProperty("inheritsFrom", out var prop) &&
                prop.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var value = prop.GetString()?.Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamePaths] TryGetInheritsFrom failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Walks <c>inheritsFrom</c> to the root profile id (usually the vanilla Minecraft version).
    /// </summary>
    public static string ResolveBaseGameVersion(string versionId, string? minecraftRoot = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return string.Empty;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { versionId };
        var current = versionId.Trim();
        while (true)
        {
            var parent = TryGetInheritsFrom(current, minecraftRoot);
            if (parent is null || !visited.Add(parent))
                break;
            current = parent;
        }

        return current;
    }

    public static bool HasMarker(string versionId, string markerFile, string? minecraftRoot = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return false;

        var path = Path.Combine(GetVersionsRoot(minecraftRoot), versionId, markerFile);
        return File.Exists(path);
    }

    public static bool IsDependencyOnly(string versionId, string? minecraftRoot = null) =>
        HasMarker(versionId, DependencyMarker, minecraftRoot) &&
        !HasMarker(versionId, UserInstanceMarker, minecraftRoot);

    public static bool IsUserInstance(string versionId, string? minecraftRoot = null) =>
        HasMarker(versionId, UserInstanceMarker, minecraftRoot);

    /// <summary>
    /// portable: vanilla pulled in by Forge/Fabric is a dependency until the user
    /// explicitly installs it as their own instance.
    /// </summary>
    public static void MarkAsDependency(string versionId, string? minecraftRoot = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return;
        if (IsUserInstance(versionId, minecraftRoot))
            return;

        try
        {
            var dir = Path.Combine(GetVersionsRoot(minecraftRoot), versionId);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, DependencyMarker), "dependency");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamePaths] MarkAsDependency failed: {ex.Message}");
        }
    }

    /// <summary>Promote a folder to a listed user instance (e.g. explicit vanilla install).</summary>
    public static void MarkAsUserInstance(string versionId, string? minecraftRoot = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return;

        try
        {
            var dir = Path.Combine(GetVersionsRoot(minecraftRoot), versionId);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, UserInstanceMarker), "user");
            var dep = Path.Combine(dir, DependencyMarker);
            if (File.Exists(dep))
                File.Delete(dep);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamePaths] MarkAsUserInstance failed: {ex.Message}");
        }
    }

    /// <summary>List folder names under <c>versions/</c> (including incomplete).</summary>
    public static IReadOnlyList<string> ListVersionFolderNames(string? minecraftRoot = null)
    {
        var versionsRoot = GetVersionsRoot(minecraftRoot);
        if (!Directory.Exists(versionsRoot))
            return [];

        return Directory.EnumerateDirectories(versionsRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name) &&
                           !name.StartsWith(TrashPrefix, StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .ToList();
    }

    /// <summary>
    /// Delete a user-installed version/instance (quarantine then remove).
    /// </summary>
    public static Task DeleteInstalledVersionAsync(
        string versionId,
        string? minecraftRoot = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return Task.CompletedTask;

        return QuarantineAndDeleteVersionAsync(versionId.Trim(), minecraftRoot, cancellationToken);
    }

    /// <summary>
    /// Remove a cancelled / incomplete version. Renames away first (survives file locks),
    /// then deletes; schedules extra background passes.
    /// </summary>
    public static async Task PurgeCancelledVersionAsync(
        string versionId,
        bool wasAlreadyInstalled,
        string? minecraftRoot = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return;

        // Never delete a complete install that existed before the cancelled job.
        if (wasAlreadyInstalled && IsVersionFullyInstalled(versionId, minecraftRoot))
            return;

        // If somehow complete now but this job was a fresh download, still remove …        // cancel means user does not want this install.
        if (!wasAlreadyInstalled)
        {
            await QuarantineAndDeleteVersionAsync(versionId, minecraftRoot, cancellationToken)
                .ConfigureAwait(false);

            // CmlLib / AV may recreate or hold locks briefly …keep sweeping.
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var delayMs in new[] { 500, 1500, 3000, 6000, 12000 })
                    {
                        await Task.Delay(delayMs).ConfigureAwait(false);
                        if (!VersionFolderExists(versionId, minecraftRoot))
                            return;

                        await QuarantineAndDeleteVersionAsync(versionId, minecraftRoot)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamePaths] Background purge '{versionId}' failed: {ex}");
                }
            });
        }
        else if (!IsVersionFullyInstalled(versionId, minecraftRoot))
        {
            // Was installed before but is now incomplete …clean the broken leftover.
            await QuarantineAndDeleteVersionAsync(versionId, minecraftRoot, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Startup / idle sweep: drop incomplete version folders and any <c>_trash_*</c> dirs.
    /// </summary>
    public static void PurgeIncompleteAndTrash(string? minecraftRoot = null)
    {
        var versionsRoot = GetVersionsRoot(minecraftRoot);
        if (!Directory.Exists(versionsRoot))
            return;

        foreach (var dir in Directory.EnumerateDirectories(versionsRoot))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name))
                continue;

            try
            {
                if (name.StartsWith(TrashPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    DeleteDirectoryBestEffort(dir);
                    continue;
                }

                var json = Path.Combine(dir, name + ".json");
                var jar = Path.Combine(dir, name + ".jar");
                if (File.Exists(json) && (File.Exists(jar) || IsVersionProfileJson(json)))
                    continue;

                // Incomplete (json-only junk, empty, etc.)
                QuarantineDirectory(dir);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamePaths] Startup purge '{name}' failed: {ex.Message}");
            }
        }

        // Second pass: delete quarantined folders.
        foreach (var dir in Directory.EnumerateDirectories(versionsRoot))
        {
            var name = Path.GetFileName(dir);
            if (name is null || !name.StartsWith(TrashPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                DeleteDirectoryBestEffort(dir);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamePaths] Trash delete '{name}' failed: {ex.Message}");
            }
        }
    }

    public static bool VersionFolderExists(string versionId, string? minecraftRoot = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return false;

        var dir = Path.Combine(GetVersionsRoot(minecraftRoot), versionId);
        return Directory.Exists(dir);
    }

    /// <summary>
    /// Clone or rename a version folder so folder name, json/jar names, and json <c>id</c> match <paramref name="targetId"/>.
    /// When <paramref name="copy"/> is true the source is kept (needed if vanilla already existed).
    /// </summary>
    public static void PublishVersionAs(
        string sourceId,
        string targetId,
        bool copy,
        string? minecraftRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        if (string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase))
            return;

        var versionsRoot = GetVersionsRoot(minecraftRoot);
        var sourceDir = Path.Combine(versionsRoot, sourceId);
        var targetDir = Path.Combine(versionsRoot, targetId);

        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException(Loc.Format(LocKeys.Error_VersionFolderNotFound, sourceId));

        if (Directory.Exists(targetDir))
            throw new IOException(Loc.Format(LocKeys.Error_VersionAlreadyExists, targetId));

        if (copy)
            CopyDirectory(sourceDir, targetDir);
        else
            Directory.Move(sourceDir, targetDir);

        RenameVersionFiles(targetDir, sourceId, targetId);
        PatchVersionJsonId(Path.Combine(targetDir, targetId + ".json"), targetId);
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    /// <summary>
    /// Loader profiles (Fabric/Forge/NeoForge) are valid with json only when they inherit
    /// from another version or declare launch metadata.
    /// </summary>
    private static bool IsVersionProfileJson(string jsonPath)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("inheritsFrom", out _))
                return true;

            if (root.TryGetProperty("mainClass", out _))
                return true;

            if (root.TryGetProperty("libraries", out var libs) &&
                libs.ValueKind == System.Text.Json.JsonValueKind.Array &&
                libs.GetArrayLength() > 0)
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void RenameVersionFiles(string versionDir, string fromId, string toId)
    {
        foreach (var ext in new[] { ".json", ".jar" })
        {
            var from = Path.Combine(versionDir, fromId + ext);
            var to = Path.Combine(versionDir, toId + ext);
            if (File.Exists(from) && !File.Exists(to))
                File.Move(from, to);
        }
    }

    private static void PatchVersionJsonId(string jsonPath, string targetId)
    {
        if (!File.Exists(jsonPath))
            return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
            using var stream = File.Create(jsonPath);
            using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            var wroteId = false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("id"))
                {
                    writer.WriteString("id", targetId);
                    wroteId = true;
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }

            if (!wroteId)
                writer.WriteString("id", targetId);

            writer.WriteEndObject();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamePaths] Patch version id failed: {ex.Message}");
            throw;
        }
    }

    private static async Task QuarantineAndDeleteVersionAsync(
        string versionId,
        string? minecraftRoot,
        CancellationToken cancellationToken = default)
    {
        var versionsRoot = GetVersionsRoot(minecraftRoot);
        var dir = Path.Combine(versionsRoot, versionId);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(dir))
            {
                // Still clear any matching trash from earlier attempts.
                DeleteMatchingTrash(versionsRoot, versionId);
                return;
            }

            try
            {
                if (QuarantineDirectory(dir))
                {
                    DeleteMatchingTrash(versionsRoot, versionId);
                    if (!Directory.Exists(dir))
                        return;
                }
                else
                {
                    DeleteDirectoryBestEffort(dir);
                    if (!Directory.Exists(dir))
                        return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamePaths] Quarantine '{versionId}' attempt {attempt + 1}: {ex.Message}");
            }

            await Task.Delay(150 + attempt * 100, cancellationToken).ConfigureAwait(false);
        }

        DeleteMatchingTrash(versionsRoot, versionId);

        if (Directory.Exists(dir))
        {
            throw new IOException(
                Loc.Format(LocKeys.Error_VersionDeleteLocked, versionId));
        }
    }

    /// <summary>Rename <paramref name="dir"/> to <c>_trash_...</c> so it vanishes from the version list immediately.</summary>
    private static bool QuarantineDirectory(string dir)
    {
        if (!Directory.Exists(dir))
            return true;

        var parent = Path.GetDirectoryName(dir);
        if (string.IsNullOrEmpty(parent))
            return false;

        var name = Path.GetFileName(dir);
        var trash = Path.Combine(parent, $"{TrashPrefix}{name}_{Guid.NewGuid():N}");

        try
        {
            // Clear read-only on the root so Move is more likely to succeed.
            try
            {
                var attrs = File.GetAttributes(dir);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(dir, attrs & ~FileAttributes.ReadOnly);
            }
            catch
            {
                // ignore
            }

            Directory.Move(dir, trash);

            try
            {
                DeleteDirectoryBestEffort(trash);
            }
            catch
            {
                // Trash can be removed on next sweep.
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamePaths] Move to trash failed for '{dir}': {ex.Message}");
            return false;
        }
    }

    private static void DeleteMatchingTrash(string versionsRoot, string versionId)
    {
        var prefix = TrashPrefix + versionId + "_";
        foreach (var dir in Directory.EnumerateDirectories(versionsRoot))
        {
            var name = Path.GetFileName(dir);
            if (name is null || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                DeleteDirectoryBestEffort(dir);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void DeleteDirectoryBestEffort(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var path in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories)
                     .OrderByDescending(p => p.Length))
        {
            try
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);

                if ((attrs & FileAttributes.Directory) != 0)
                    continue;

                File.Delete(path);
            }
            catch
            {
                // retry later
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)
                     .OrderByDescending(p => p.Length))
        {
            try
            {
                Directory.Delete(sub, recursive: true);
            }
            catch
            {
                // retry later
            }
        }

        Directory.Delete(dir, recursive: true);
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var hasSolution =
                File.Exists(Path.Combine(dir.FullName, "Ardel.slnx")) ||
                File.Exists(Path.Combine(dir.FullName, "Ardel.sln"));

            var looksLikeRepo =
                File.Exists(Path.Combine(dir.FullName, "design.md")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src", "Ardel.Launcher"));

            if (hasSolution || looksLikeRepo)
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }
}

