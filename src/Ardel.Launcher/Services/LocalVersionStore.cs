using System.Text.Json;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>
/// Fast local version scan with zero CmlLib dependency (keeps cold start light).
/// Each version profile JSON is read and parsed at most once.
/// </summary>
public sealed class LocalVersionStore
{
    public LocalVersionStore(InstanceSettingsStore instanceSettings)
    {
        // Kept for DI compatibility; list scan avoids Load() (no CreateDirectory side effects).
        _ = instanceSettings;
    }

    public IReadOnlyList<GameVersionItem> GetInstalled(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            return [];

        var versionsDir = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDir))
            return [];

        var candidates = new List<(GameVersionItem Item, string? InheritsFrom)>();
        foreach (var dir in Directory.EnumerateDirectories(versionsDir))
        {
            var id = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(id))
                continue;

            if (id.StartsWith(GamePaths.TrashPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var jsonPath = Path.Combine(dir, id + ".json");
            if (!File.Exists(jsonPath))
                continue;

            string profileJson;
            try
            {
                profileJson = File.ReadAllText(jsonPath);
            }
            catch
            {
                continue;
            }

            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(profileJson);
            }
            catch
            {
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                var jarPath = Path.Combine(dir, id + ".jar");
                if (!File.Exists(jarPath) && !LooksLikeProfile(root))
                    continue;

                string? inheritsFrom = null;
                if (root.TryGetProperty("inheritsFrom", out var inherits) &&
                    inherits.ValueKind == JsonValueKind.String)
                {
                    var value = inherits.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        inheritsFrom = value.Trim();
                }

                var kind = VersionKindDetector.DetectFromJsonElement(root);
                if (kind == VersionKind.Vanilla)
                    kind = VersionKindDetector.DetectFromId(id);

                var (notes, iconGlyph) = InstanceSettingsStore.TryReadListFields(dir);
                var iconPath = InstanceIconHelper.FindPath(dir);

                candidates.Add((
                    new GameVersionItem
                    {
                        Id = id,
                        Type = "local",
                        Kind = kind,
                        IsInstalled = true,
                        ReleaseTime = File.GetLastWriteTimeUtc(jsonPath),
                        Notes = notes,
                        IconPath = iconPath,
                        IconGlyph = iconGlyph,
                        OfficialJavaMajor = OfficialJavaRequirements.TryGetCached(id, out var cachedMajor)
                            ? cachedMajor
                            : 0
                    },
                    inheritsFrom));
            }
        }

        var parentIds = new HashSet<string>(
            candidates
                .Select(c => c.InheritsFrom)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!),
            StringComparer.OrdinalIgnoreCase);

        var items = candidates
            .Where(c => ShouldListInstance(c.Item, parentIds, gameDirectory))
            .Select(c => c.Item)
            .ToList();

        items.Sort((a, b) =>
            (b.ReleaseTime ?? DateTimeOffset.MinValue).CompareTo(a.ReleaseTime ?? DateTimeOffset.MinValue));
        return items;
    }

    private static bool LooksLikeProfile(JsonElement root) =>
        root.TryGetProperty("id", out _) ||
        root.TryGetProperty("libraries", out _) ||
        root.TryGetProperty("mainClass", out _) ||
        root.TryGetProperty("inheritsFrom", out _);

    private static bool ShouldListInstance(
        GameVersionItem item,
        HashSet<string> parentIds,
        string gameDirectory)
    {
        if (GamePaths.IsUserInstance(item.Id, gameDirectory))
            return true;

        if (GamePaths.IsDependencyOnly(item.Id, gameDirectory))
            return false;

        if (parentIds.Contains(item.Id) && item.Kind == VersionKind.Vanilla)
            return false;

        return true;
    }
}
