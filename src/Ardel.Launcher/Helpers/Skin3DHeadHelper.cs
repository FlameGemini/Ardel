using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Media.Imaging;
using MinecraftSkinRender.Image;
using SkiaSharp;
using Windows.Storage.Streams;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Renders a Minecraft skin head via Coloryr/MinecraftSkinRender (<c>Skin3DHeadTypeA</c>)
/// into a WinUI <see cref="BitmapImage"/>.
/// </summary>
public static class Skin3DHeadHelper
{
    private static readonly ConcurrentDictionary<string, (DateTime LastWriteUtc, byte[] PngBytes)> s_pngCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Isometric 3D head preview (400×400 from upstream TypeA). Prefer calling from the UI thread.</summary>
    public static async Task<BitmapImage?> TryCreateAsync(
        string? pngPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath))
            return null;

        try
        {
            var lastWriteUtc = File.GetLastWriteTimeUtc(pngPath);
            byte[]? pngBytes = null;

            if (s_pngCache.TryGetValue(pngPath, out var cached) && cached.LastWriteUtc == lastWriteUtc)
            {
                pngBytes = cached.PngBytes;
            }
            else
            {
                pngBytes = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var skin = LoadNormalizedSkin(pngPath);
                    if (skin is null)
                        return null;

                    using var skImage = Skin3DHeadTypeA.MakeHeadImage(skin);
                    using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
                    return encoded.ToArray();
                }, cancellationToken).ConfigureAwait(true);

                if (pngBytes is not null && pngBytes.Length > 0)
                {
                    s_pngCache[pngPath] = (lastWriteUtc, pngBytes);
                }
            }

            if (pngBytes is null || pngBytes.Length == 0)
                return null;

            return await BitmapImageFromPngAsync(pngBytes, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Skin3DHead] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Load PNG bytes into <see cref="BitmapImage"/> without disposing the backing
    /// <see cref="InMemoryRandomAccessStream"/> before <see cref="BitmapImage.SetSourceAsync"/>.
    /// </summary>
    internal static async Task<BitmapImage?> BitmapImageFromPngAsync(
        byte[] pngBytes,
        CancellationToken cancellationToken = default)
    {
        var dq = App.MainWindowInstance?.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dq is not null && !dq.HasThreadAccess)
        {
            var tcs = new TaskCompletionSource<BitmapImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ok = dq.TryEnqueue(async () =>
            {
                try
                {
                    var bmp = await CreateBitmapImageInternalAsync(pngBytes, cancellationToken).ConfigureAwait(true);
                    tcs.TrySetResult(bmp);
                }
                catch (Exception)
                {
                    tcs.TrySetResult(null);
                }
            });
            if (!ok)
                return null;
            return await tcs.Task.ConfigureAwait(false);
        }

        return await CreateBitmapImageInternalAsync(pngBytes, cancellationToken).ConfigureAwait(true);
    }

    private static async Task<BitmapImage> CreateBitmapImageInternalAsync(
        byte[] pngBytes,
        CancellationToken cancellationToken)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(true);
            writer.DetachStream();
        }

        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream).AsTask(cancellationToken).ConfigureAwait(true);
        return image;
    }

    /// <summary>Scale HD skins down to 64×N so UV rects in Skin3DHead* stay correct.</summary>
    private static SKBitmap? LoadNormalizedSkin(string pngPath)
    {
        using var stream = File.OpenRead(pngPath);
        using var decoded = SKBitmap.Decode(stream);
        if (decoded is null || decoded.Width < 64 || decoded.Height < 32)
            return null;

        if (decoded.Width == 64)
            return decoded.Copy();

        var scale = decoded.Width / 64;
        if (scale <= 1)
            return decoded.Copy();

        var targetH = Math.Max(32, decoded.Height / scale);
        return decoded.Resize(
            new SKImageInfo(64, targetH, SKColorType.Rgba8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Nearest));
    }
}