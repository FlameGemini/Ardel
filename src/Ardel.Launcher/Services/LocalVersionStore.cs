using System.Text.Json;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>
/// Fast local version scan with zero CmlLib dependency (keeps cold start light).
/// </summary>
public sealed class LocalVersionStore
{
    private readonly InstanceSettingsStore _instanceSettings;

    public LocalVersionStore(InstanceSettingsStore instanceSettings)
    {
        _instanceSettings = instanceSettings;
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

            if (!GamePaths.IsVersionFullyInstalled(id, gameDirectory))
                continue;

            var jsonPath = Path.Combine(dir, id + ".json");
            var notes = _instanceSettings.Load(id, gameDirectory).Notes ?? string.Empty;
            var iconPath = InstanceIconHelper.FindPath(dir);
            candidates.Add((
                new GameVersionItem
                {
                    Id = id,
                    Type = "local",
                    Kind = VersionKindDetector.Detect(id, gameDirectory),
                    IsInstalled = true,
                    ReleaseTime = File.GetLastWriteTimeUtc(jsonPath),
                    Notes = notes,
                    IconPath = iconPath
                },
                ReadInheritsFrom(jsonPath)));
        }

        var parentIds = new HashSet<string>(
            candidates
                .Select(c => c.InheritsFrom)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!),
            StringComparer.OrdinalIgnoreCase);

        // portable listing:
        // - Loader parents marked as dependencies stay hidden
        // - Explicit user instances (.ardel-user) always show …even if named 1.21.11
        // - Legacy (no markers): hide vanilla folders that are only inheritsFrom parents
        var items = candidates
            .Where(c => ShouldListInstance(c.Item, parentIds, gameDirectory))
            .Select(c => c.Item)
            .ToList();

        items.Sort((a, b) =>
            (b.ReleaseTime ?? DateTimeOffset.MinValue).CompareTo(a.ReleaseTime ?? DateTimeOffset.MinValue));
        return items;
    }

    private static bool ShouldListInstance(
        GameVersionItem item,
        HashSet<string> parentIds,
        string gameDirectory)
    {
        if (GamePaths.IsUserInstance(item.Id, gameDirectory))
            return true;

        if (GamePaths.IsDependencyOnly(item.Id, gameDirectory))
            return false;

        // Legacy installs without markers: hide pure vanilla parents of loaders.
        if (parentIds.Contains(item.Id) && item.Kind == VersionKind.Vanilla)
            return false;

        return true;
    }

    private static string? ReadInheritsFrom(string jsonPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (doc.RootElement.TryGetProperty("inheritsFrom", out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }
        catch
        {
            // ignore malformed profiles
        }

        return null;
    }
}

