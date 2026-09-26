using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Per-instance icon file under the version folder: <c>ardel-icon.{ext}</c>.
/// Caches decoded <see cref="BitmapImage"/> objects in memory so returning to the
/// Profiles/Instances list is instant without re-reading or re-decoding from disk.
/// </summary>
public static class InstanceIconHelper
{
    public const string FilePrefix = "ardel-icon";

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Evict cached bitmap(s) when the icon file on disk is modified or deleted.</summary>
    public static void Invalidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        Cache.TryRemove(path, out _);
        foreach (var key in Cache.Keys)
        {
            if (key.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                Cache.TryRemove(key, out _);
        }
    }

    public static void ClearCache() => Cache.Clear();

    public static string? FindPath(string versionDirectory)
    {
        if (string.IsNullOrWhiteSpace(versionDirectory) || !Directory.Exists(versionDirectory))
            return null;

        foreach (var ext in Extensions)
        {
            var path = Path.Combine(versionDirectory, FilePrefix + ext);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public static void Clear(string versionDirectory)
    {
        if (string.IsNullOrWhiteSpace(versionDirectory) || !Directory.Exists(versionDirectory))
            return;

        foreach (var ext in Extensions)
        {
            var path = Path.Combine(versionDirectory, FilePrefix + ext);
            Invalidate(path);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // best-effort
            }
        }
    }

    public static string SetFromFile(string versionDirectory, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(sourcePath);

        Directory.CreateDirectory(versionDirectory);
        Clear(versionDirectory);

        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext) ||
            !Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            ext = ".png";

        var dest = Path.Combine(versionDirectory, FilePrefix + ext.ToLowerInvariant());
        Invalidate(dest);
        File.Copy(sourcePath, dest, overwrite: true);
        return dest;
    }

    public static BitmapImage? CreateImage(string? path, int decodePixels = 64)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var key = $"{path}|{decodePixels}";
        return Cache.GetOrAdd(key, static k =>
        {
            var sep = k.LastIndexOf('|');
            var pixels = sep > 0 && int.TryParse(k.AsSpan(sep + 1), out var p) ? p : 64;
            var filePath = sep > 0 ? k[..sep] : k;
            try
            {
                return new BitmapImage
                {
                    DecodePixelWidth = pixels,
                    DecodePixelHeight = pixels,
                    DecodePixelType = DecodePixelType.Logical,
                    CreateOptions = BitmapCreateOptions.None,
                    UriSource = new Uri(filePath)
                };
            }
            catch
            {
                return new BitmapImage();
            }
        });
    }
}
