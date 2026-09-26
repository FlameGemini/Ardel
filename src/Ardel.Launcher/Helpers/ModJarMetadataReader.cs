using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ardel.Launcher.Helpers;

public sealed record ModJarMetadata(string? ModId, string? Title, string FileName, string FullPath);

public static class ModJarMetadataReader
{
    public static ModJarMetadata? Read(string jarPath)
    {
        if (!File.Exists(jarPath))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            string? title = null;
            string? modId = null;

            if (TryReadFabric(archive, ref title, ref modId) ||
                TryReadQuilt(archive, ref title, ref modId) ||
                TryReadForge(archive, ref title, ref modId))
            {
                return new ModJarMetadata(
                    modId,
                    title ?? Path.GetFileNameWithoutExtension(jarPath),
                    Path.GetFileName(jarPath),
                    jarPath);
            }
        }
        catch
        {
            // ignore
        }

        return new ModJarMetadata(
            null,
            Path.GetFileNameWithoutExtension(jarPath),
            Path.GetFileName(jarPath),
            jarPath);
    }

    public static IReadOnlyList<ModJarMetadata> ScanModsFolder(string modsDirectory)
    {
        if (!Directory.Exists(modsDirectory))
            return [];

        return Directory.EnumerateFiles(modsDirectory)
            .Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                        !p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
            .Select(Read)
            .Where(m => m is not null)
            .Cast<ModJarMetadata>()
            .ToList();
    }

    private static bool TryReadFabric(ZipArchive archive, ref string? title, ref string? modId)
    {
        var entry = archive.GetEntry("fabric.mod.json");
        if (entry is null)
            return false;

        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            modId = idProp.GetString();
        if (doc.RootElement.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            title = nameProp.GetString();
        return true;
    }

    private static bool TryReadQuilt(ZipArchive archive, ref string? title, ref string? modId)
    {
        var entry = archive.GetEntry("quilt.mod.json");
        if (entry is null)
            return false;

        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("quilt_loader", out var loaderProp))
            return false;

        if (loaderProp.TryGetProperty("id", out var qid) && qid.ValueKind == JsonValueKind.String)
            modId = qid.GetString();

        if (loaderProp.TryGetProperty("metadata", out var metaProp) &&
            metaProp.TryGetProperty("name", out var nameProp) &&
            nameProp.ValueKind == JsonValueKind.String)
            title = nameProp.GetString();

        return true;
    }

    private static bool TryReadForge(ZipArchive archive, ref string? title, ref string? modId)
    {
        var entry = archive.GetEntry("META-INF/mods.toml") ?? archive.GetEntry("META-INF/neoforge.mods.toml");
        if (entry is null)
            return false;

        using var reader = new StreamReader(entry.Open());
        var content = reader.ReadToEnd();

        var modIdMatch = Regex.Match(content, @"modId\s*=\s*""([^""]+)""");
        if (modIdMatch.Success)
            modId = modIdMatch.Groups[1].Value;

        var displayMatch = Regex.Match(content, @"displayName\s*=\s*""([^""]+)""");
        if (displayMatch.Success)
            title = displayMatch.Groups[1].Value;

        return true;
    }
}
