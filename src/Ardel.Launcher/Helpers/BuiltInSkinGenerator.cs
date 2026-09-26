using System.IO.Compression;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Seeds Steve / Alex from the real client jar under <c>.minecraft/versions</c>
/// (wide/steve + slim/alex). Falls back to bundled copies next to the app.
/// </summary>
public static class BuiltInSkinGenerator
{
    private const string JarSteveEntry = "assets/minecraft/textures/entity/player/wide/steve.png";
    private const string JarAlexEntry = "assets/minecraft/textures/entity/player/slim/alex.png";

    public static Task WriteSteveAsync(string path, CancellationToken cancellationToken = default) =>
        WriteFromOfficialAsync(path, JarSteveEntry, "steve.png", cancellationToken);

    public static Task WriteAlexAsync(string path, CancellationToken cancellationToken = default) =>
        WriteFromOfficialAsync(path, JarAlexEntry, "alex.png", cancellationToken);

    public static Task WriteEmptyAsync(string path, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Minimal valid empty transparent PNG is overkill — write zero-length then overwrite
        // with a 1×1 transparent via copy of a tiny buffer from bundled empty if needed.
        // Keep a real transparent 64×64 by copying steve and clearing is wrong; use tiny file.
        return WriteMinimalTransparentPngAsync(path, cancellationToken);
    }

    private static async Task WriteFromOfficialAsync(
        string destPath,
        string jarEntry,
        string bundledFileName,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        if (TryExtractFromClientJar(jarEntry, destPath))
            return;

        var bundled = FindBundledSkin(bundledFileName);
        if (bundled is not null)
        {
            File.Copy(bundled, destPath, overwrite: true);
            return;
        }

        throw new FileNotFoundException(
            $"Missing official skin '{jarEntry}'. Install a Minecraft version under .minecraft/versions, " +
            $"or ship Assets/Skins/{bundledFileName}.");
    }

    private static bool TryExtractFromClientJar(string entryName, string destPath)
    {
        try
        {
            var versionsRoot = Path.Combine(
                Services.GamePaths.GetMinecraftRoot(),
                "versions");
            if (!Directory.Exists(versionsRoot))
                return false;

            // Prefer jars whose name matches the folder (vanilla clients).
            foreach (var dir in Directory.EnumerateDirectories(versionsRoot)
                         .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)))
            {
                var id = Path.GetFileName(dir);
                var preferred = Path.Combine(dir, id + ".jar");
                if (File.Exists(preferred) && TryExtractEntry(preferred, entryName, destPath))
                    return true;

                foreach (var jar in Directory.EnumerateFiles(dir, "*.jar"))
                {
                    if (TryExtractEntry(jar, entryName, destPath))
                        return true;
                }
            }
        }
        catch
        {
            // fall through
        }

        return false;
    }

    private static bool TryExtractEntry(string jarPath, string entryName, string destPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            var entry = zip.GetEntry(entryName);
            if (entry is null)
                return false;

            entry.ExtractToFile(destPath, overwrite: true);
            return File.Exists(destPath) && new FileInfo(destPath).Length > 64;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindBundledSkin(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Skins", fileName),
            Path.Combine(AppContext.BaseDirectory, "Skins", fileName),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task WriteMinimalTransparentPngAsync(string path, CancellationToken cancellationToken)
    {
        // 1×1 transparent PNG
        byte[] png =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
        await File.WriteAllBytesAsync(path, png, cancellationToken).ConfigureAwait(false);
    }
}
