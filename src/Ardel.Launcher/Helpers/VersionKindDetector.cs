using System.Text.Json;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Detects vanilla / Fabric / Forge / NeoForge / OptiFine from profile JSON (not folder name).
/// Folder names like <c>1.21.1</c> are often kept by users even for loader profiles.
/// </summary>
public static class VersionKindDetector
{
    public static VersionKind Detect(string versionId, string? minecraftRoot = null)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return VersionKind.Vanilla;

        try
        {
            var root = string.IsNullOrWhiteSpace(minecraftRoot)
                ? Services.GamePaths.GetMinecraftRoot()
                : minecraftRoot;
            var jsonPath = Path.Combine(root, "versions", versionId, versionId + ".json");
            if (File.Exists(jsonPath))
            {
                var kind = DetectFromJson(File.ReadAllText(jsonPath));
                if (kind != VersionKind.Vanilla)
                    return kind;
            }
        }
        catch
        {
            // fall through to id heuristics
        }

        return DetectFromId(versionId);
    }

    public static VersionKind DetectFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return VersionKind.Vanilla;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                var fromId = DetectFromId(idProp.GetString() ?? string.Empty);
                if (fromId != VersionKind.Vanilla)
                    return fromId;
            }

            if (root.TryGetProperty("mainClass", out var main) && main.ValueKind == JsonValueKind.String)
            {
                var mainClass = main.GetString() ?? string.Empty;
                if (mainClass.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                    return VersionKind.Fabric;
                if (mainClass.Contains("neoforge", StringComparison.OrdinalIgnoreCase) ||
                    mainClass.Contains("neoform", StringComparison.OrdinalIgnoreCase))
                    return VersionKind.NeoForge;
                if (mainClass.Contains("minecraftforge", StringComparison.OrdinalIgnoreCase) ||
                    mainClass.Contains("cpw.mods", StringComparison.OrdinalIgnoreCase))
                    return VersionKind.Forge;
                if (mainClass.Contains("optifine", StringComparison.OrdinalIgnoreCase))
                    return VersionKind.OptiFine;
            }

            if (root.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
            {
                foreach (var lib in libs.EnumerateArray())
                {
                    var name = lib.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString() ?? string.Empty
                        : string.Empty;
                    if (name.StartsWith("net.fabricmc:", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(":fabric-loader:", StringComparison.OrdinalIgnoreCase))
                        return VersionKind.Fabric;
                    if (name.StartsWith("net.neoforged:", StringComparison.OrdinalIgnoreCase))
                        return VersionKind.NeoForge;
                    if (name.StartsWith("net.minecraftforge:", StringComparison.OrdinalIgnoreCase))
                        return VersionKind.Forge;
                    if (name.Contains("optifine", StringComparison.OrdinalIgnoreCase))
                        return VersionKind.OptiFine;
                }
            }
        }
        catch
        {
            // ignore malformed json
        }

        return VersionKind.Vanilla;
    }

    public static VersionKind DetectFromId(string id)
    {
        if (id.Contains("fabric", StringComparison.OrdinalIgnoreCase))
            return VersionKind.Fabric;
        if (id.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            return VersionKind.NeoForge;
        if (id.Contains("forge", StringComparison.OrdinalIgnoreCase))
            return VersionKind.Forge;
        if (id.Contains("optifine", StringComparison.OrdinalIgnoreCase))
            return VersionKind.OptiFine;
        if (id.Contains("quilt", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("liteloader", StringComparison.OrdinalIgnoreCase))
            return VersionKind.Custom;
        return VersionKind.Vanilla;
    }

    /// <summary>Suggested versions/ folder name when installing a loader.</summary>
    public static string SuggestName(string minecraftVersionId, ModLoaderKind loader, string? loaderVersion = null)
    {
        var mc = minecraftVersionId.Trim();
        return loader switch
        {
            ModLoaderKind.Fabric when !string.IsNullOrWhiteSpace(loaderVersion) =>
                FabricInstallerVersionName(mc, loaderVersion.Trim()),
            ModLoaderKind.Fabric => $"{mc}-fabric",
            ModLoaderKind.Forge when !string.IsNullOrWhiteSpace(loaderVersion) =>
                $"{mc}-forge-{Sanitize(loaderVersion)}",
            ModLoaderKind.Forge => $"{mc}-forge",
            ModLoaderKind.NeoForge when !string.IsNullOrWhiteSpace(loaderVersion) =>
                $"{mc}-neoforge-{Sanitize(loaderVersion)}",
            ModLoaderKind.NeoForge => $"{mc}-neoforge",
            ModLoaderKind.OptiFine when !string.IsNullOrWhiteSpace(loaderVersion) =>
                $"{mc}-OptiFine-{Sanitize(loaderVersion)}",
            ModLoaderKind.OptiFine => $"{mc}-OptiFine",
            _ => mc
        };
    }

    private static string FabricInstallerVersionName(string mc, string loaderVersion) =>
        "fabric-loader-" + loaderVersion + "-" + mc;

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '-');
        return value.Replace(' ', '-');
    }
}
