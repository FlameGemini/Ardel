using Microsoft.UI.Xaml.Media.Imaging;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Per-instance icon file under the version folder: <c>ardel-icon.{ext}</c>.
/// </summary>
public static class InstanceIconHelper
{
    public const string FilePrefix = "ardel-icon";

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

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
        File.Copy(sourcePath, dest, overwrite: true);
        return dest;
    }

    public static BitmapImage? CreateImage(string? path, int decodePixels = 64)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return new BitmapImage
            {
                DecodePixelWidth = decodePixels,
                DecodePixelHeight = decodePixels,
                DecodePixelType = DecodePixelType.Logical,
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                UriSource = new Uri(path)
            };
        }
        catch
        {
            return null;
        }
    }
}
