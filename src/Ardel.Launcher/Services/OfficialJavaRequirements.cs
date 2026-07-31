using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>
/// Resolves the Java major version from official Minecraft version metadata
/// (<c>javaVersion.majorVersion</c>), with a local disk cache.
/// </summary>
internal static class OfficialJavaRequirements
{
    private static readonly ConcurrentDictionary<string, int> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object DiskGate = new();
    private static bool _diskLoaded;

    private static string CachePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            "java_major_cache.json");

    public static bool TryGetCached(string versionId, out int major)
    {
        EnsureDiskLoaded();
        return Memory.TryGetValue(versionId, out major) && major > 0;
    }

    public static void Remember(string versionId, int major)
    {
        if (string.IsNullOrWhiteSpace(versionId) || major <= 0)
            return;

        Memory[versionId] = major;
        ScheduleDiskSave();
    }

    /// <summary>Apply cached majors onto UI items (no network).</summary>
    public static void ApplyCache(IEnumerable<GameVersionItem> items)
    {
        EnsureDiskLoaded();
        foreach (var item in items)
        {
            if (item.OfficialJavaMajor is > 0)
            {
                Remember(item.Id, item.OfficialJavaMajor.Value);
                continue;
            }

            if (TryGetCached(item.Id, out var major))
                item.OfficialJavaMajor = major;
        }
    }

    /// <summary>
    /// Read <c>javaVersion.majorVersion</c> from a local profile, following <c>inheritsFrom</c>.
    /// </summary>
    public static int? TryReadLocal(string versionId, string? minecraftRoot = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return null;

        if (TryGetCached(versionId, out var cached))
            return cached;

        var root = string.IsNullOrWhiteSpace(minecraftRoot)
            ? GamePaths.GetMinecraftRoot()
            : minecraftRoot;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = versionId;

        while (!string.IsNullOrWhiteSpace(current) && visited.Add(current))
        {
            var jsonPath = Path.Combine(root, "versions", current, current + ".json");
            if (!File.Exists(jsonPath))
                break;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var rootEl = doc.RootElement;
                if (TryReadMajor(rootEl, out var major))
                {
                    Remember(versionId, major);
                    Remember(current, major);
                    return major;
                }

                if (rootEl.TryGetProperty("inheritsFrom", out var parent) &&
                    parent.ValueKind == JsonValueKind.String)
                {
                    current = parent.GetString();
                    continue;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficialJavaRequirements] Local read {current}: {ex.Message}");
                break;
            }

            break;
        }

        return null;
    }

    public static async Task PrefetchAsync(
        IReadOnlyList<GameVersionItem> items,
        HttpClient http,
        CancellationToken cancellationToken = default)
    {
        EnsureDiskLoaded();
        ApplyCache(items);

        var pending = items
            .Where(i => (i.OfficialJavaMajor is null or <= 0) &&
                        !string.IsNullOrWhiteSpace(i.MetadataUrl))
            .ToList();
        if (pending.Count == 0)
            return;

        await Parallel.ForEachAsync(
            pending,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 12,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                try
                {
                    var major = await FetchMajorAsync(item.Id, item.MetadataUrl!, http, ct)
                        .ConfigureAwait(false);
                    if (major > 0)
                    {
                        // Cache only — never mutate bound GameVersionItem from worker threads
                        // (PropertyChanged storms freeze the Download list).
                        Remember(item.Id, major);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OfficialJavaRequirements] Prefetch {item.Id}: {ex.Message}");
                }
            }).ConfigureAwait(false);
    }

    public static async Task<int> ResolveAsync(
        string versionId,
        string? metadataUrl,
        string? minecraftRoot,
        HttpClient http,
        CancellationToken cancellationToken = default)
    {
        if (TryGetCached(versionId, out var cached))
            return cached;

        var local = TryReadLocal(versionId, minecraftRoot);
        if (local is > 0)
            return local.Value;

        if (!string.IsNullOrWhiteSpace(metadataUrl))
        {
            var fetched = await FetchMajorAsync(versionId, metadataUrl, http, cancellationToken)
                .ConfigureAwait(false);
            if (fetched > 0)
                return fetched;
        }

        // Very old profiles omit javaVersion — Mojang treated these as Java 8.
        Remember(versionId, 8);
        return 8;
    }

    private static async Task<int> FetchMajorAsync(
        string versionId,
        string metadataUrl,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        if (TryGetCached(versionId, out var cached))
            return cached;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        using var response = await http.GetAsync(metadataUrl, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
            .ConfigureAwait(false);

        if (TryReadMajor(doc.RootElement, out var major))
        {
            Remember(versionId, major);
            return major;
        }

        if (doc.RootElement.TryGetProperty("inheritsFrom", out var parent) &&
            parent.ValueKind == JsonValueKind.String &&
            parent.GetString() is { Length: > 0 } parentId)
        {
            // Parent id without URL — try local / cache only.
            var fromParent = TryReadLocal(parentId) ?? (TryGetCached(parentId, out var pm) ? pm : 0);
            if (fromParent > 0)
            {
                Remember(versionId, fromParent);
                return fromParent;
            }
        }

        Remember(versionId, 8);
        return 8;
    }

    private static bool TryReadMajor(JsonElement root, out int major)
    {
        major = 0;
        if (!root.TryGetProperty("javaVersion", out var java) ||
            java.ValueKind != JsonValueKind.Object)
            return false;

        if (!java.TryGetProperty("majorVersion", out var majorEl))
            return false;

        if (majorEl.ValueKind == JsonValueKind.Number && majorEl.TryGetInt32(out major) && major > 0)
            return true;

        if (majorEl.ValueKind == JsonValueKind.String &&
            int.TryParse(majorEl.GetString(), out major) &&
            major > 0)
            return true;

        return false;
    }

    private static void EnsureDiskLoaded()
    {
        if (_diskLoaded)
            return;

        lock (DiskGate)
        {
            if (_diskLoaded)
                return;

            try
            {
                var path = CachePath;
                if (File.Exists(path))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Number &&
                                prop.Value.TryGetInt32(out var major) &&
                                major > 0)
                                Memory[prop.Name] = major;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficialJavaRequirements] Cache load failed: {ex.Message}");
            }

            _diskLoaded = true;
        }
    }

    private static void ScheduleDiskSave()
    {
        // Fire-and-forget coalesce: write current snapshot.
        _ = Task.Run(static () =>
        {
            try
            {
                lock (DiskGate)
                {
                    var path = CachePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var snapshot = Memory.ToArray();
                    using var stream = File.Create(path);
                    using var writer = new Utf8JsonWriter(stream);
                    writer.WriteStartObject();
                    foreach (var (id, major) in snapshot.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                        writer.WriteNumber(id, major);
                    writer.WriteEndObject();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficialJavaRequirements] Cache save failed: {ex.Message}");
            }
        });
    }
}
