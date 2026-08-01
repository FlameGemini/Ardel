using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Reuses <see cref="BitmapImage"/> instances by URI so catalog list rebinds
/// (progressive search / virtualization) do not flash empty icons.
/// </summary>
public static class CatalogIconCache
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapImage? Get(Uri? uri, int decodePixels = 64)
    {
        if (uri is null)
            return null;

        if (uri.Scheme is not ("http" or "https"))
            return null;

        var key = uri.AbsoluteUri + "|" + decodePixels;
        return Cache.GetOrAdd(key, static (k, u) =>
        {
            var sep = k.LastIndexOf('|');
            var pixels = sep > 0 && int.TryParse(k.AsSpan(sep + 1), out var p) ? p : 64;
            return new BitmapImage
            {
                DecodePixelWidth = pixels,
                DecodePixelHeight = pixels,
                DecodePixelType = DecodePixelType.Logical,
                UriSource = u
            };
        }, uri);
    }
}
