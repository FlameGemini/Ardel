using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Crops Minecraft Java Edition 64×32 cape textures to the canonical 10:16 back face
/// and renders crisp previews for WinUI 3.
/// </summary>
public static class CapePreviewHelper
{
    private static readonly ConcurrentDictionary<string, byte[]> s_cache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<byte[]?> GetCroppedCapePngBytesAsync(
        string? capeUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(capeUrl))
            return null;

        var secureUrl = capeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + capeUrl[7..]
            : capeUrl;

        try
        {
            if (s_cache.TryGetValue(secureUrl, out var cachedBytes))
                return cachedBytes;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var rawBytes = await client.GetByteArrayAsync(secureUrl, cancellationToken).ConfigureAwait(false);
            if (rawBytes.Length < 64)
                return null;

            var pngBytes = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var original = SKBitmap.Decode(rawBytes);
                if (original is null || original.Width < 12 || original.Height < 16)
                    return null;

                var scale = Math.Max(1, original.Width / 64);
                // The visible back face of a Minecraft cape is at (1, 1, 10, 16)
                var srcRect = new SKRectI(
                    1 * scale,
                    1 * scale,
                    (1 + 10) * scale,
                    (1 + 16) * scale);

                using var cropped = new SKBitmap(10 * scale, 16 * scale);
                using (var canvas = new SKCanvas(cropped))
                {
                    canvas.DrawBitmap(original, srcRect, new SKRect(0, 0, cropped.Width, cropped.Height));
                }

                // Render cleanly scaled up to 60×96 for card display
                using var resized = cropped.Resize(
                    new SKImageInfo(60, 96, SKColorType.Rgba8888, SKAlphaType.Premul),
                    new SKSamplingOptions(SKFilterMode.Nearest));
                if (resized is null)
                    return null;

                using var image = SKImage.FromBitmap(resized);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data.ToArray();
            }, cancellationToken).ConfigureAwait(false);

            if (pngBytes is not null && pngBytes.Length > 0)
            {
                s_cache[secureUrl] = pngBytes;
            }

            return pngBytes;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CapePreview] Crop failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Must be invoked from the UI thread because BitmapImage requires UI thread creation.
    /// </summary>
    public static async Task<BitmapImage?> GetCroppedCapePreviewAsync(
        string? capeUrl,
        CancellationToken cancellationToken = default)
    {
        var bytes = await GetCroppedCapePngBytesAsync(capeUrl, cancellationToken);
        if (bytes is null || bytes.Length == 0)
            return null;

        return await Skin3DHeadHelper.BitmapImageFromPngAsync(bytes, cancellationToken);
    }
}
