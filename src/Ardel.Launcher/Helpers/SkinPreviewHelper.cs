using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Ardel.Launcher.Helpers;

/// <summary>Builds a crisp head preview from a Minecraft skin PNG (face + hat overlay).</summary>
public static class SkinPreviewHelper
{
    public static async Task<BitmapImage?> TryCreateHeadPreviewAsync(
        string? pngPath,
        int displaySize = 96,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath))
            return null;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(pngPath)
                .AsTask(cancellationToken)
                .ConfigureAwait(true);
            using var stream = await file.OpenReadAsync().AsTask(cancellationToken)
                .ConfigureAwait(true);

            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken)
                .ConfigureAwait(true);

            var width = (int)decoder.PixelWidth;
            var height = (int)decoder.PixelHeight;
            if (width < 64 || height < 32)
                return null;

            // Scale UV coords for HD skins (64×64 base).
            var scale = width / 64;
            var head = 8 * scale;

            var pixels = await decoder
                .GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage)
                .AsTask(cancellationToken)
                .ConfigureAwait(true);

            var src = pixels.DetachPixelData();
            var face = new byte[head * head * 4];
            CopyRect(src, width, 8 * scale, 8 * scale, head, head, face);

            // Hat / second layer overlay at (40,8)
            if (height >= 64 * scale || height >= 64)
            {
                var overlay = new byte[head * head * 4];
                CopyRect(src, width, 40 * scale, 8 * scale, head, head, overlay);
                CompositeOver(face, overlay);
            }

            var outPixels = NearestUpscale(face, head, head, displaySize, displaySize);

            using var outStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder
                .CreateAsync(BitmapEncoder.PngEncoderId, outStream)
                .AsTask(cancellationToken)
                .ConfigureAwait(true);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)displaySize,
                (uint)displaySize,
                96,
                96,
                Premultiply(outPixels));
            await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(true);
            outStream.Seek(0);

            var image = new BitmapImage();
            await image.SetSourceAsync(outStream).AsTask(cancellationToken).ConfigureAwait(true);
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyRect(
        byte[] src, int srcStride, int sx, int sy, int w, int h, byte[] dest)
    {
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var si = ((sy + y) * srcStride + (sx + x)) * 4;
                var di = (y * w + x) * 4;
                dest[di] = src[si];
                dest[di + 1] = src[si + 1];
                dest[di + 2] = src[si + 2];
                dest[di + 3] = src[si + 3];
            }
        }
    }

    private static void CompositeOver(byte[] baseBg, byte[] overlay)
    {
        for (var i = 0; i < baseBg.Length; i += 4)
        {
            var oa = overlay[i + 3] / 255f;
            if (oa <= 0.01f)
                continue;
            var ia = 1f - oa;
            baseBg[i] = (byte)(overlay[i] * oa + baseBg[i] * ia);
            baseBg[i + 1] = (byte)(overlay[i + 1] * oa + baseBg[i + 1] * ia);
            baseBg[i + 2] = (byte)(overlay[i + 2] * oa + baseBg[i + 2] * ia);
            baseBg[i + 3] = 255;
        }
    }

    private static byte[] NearestUpscale(byte[] src, int sw, int sh, int dw, int dh)
    {
        var dest = new byte[dw * dh * 4];
        for (var y = 0; y < dh; y++)
        {
            var sy = y * sh / dh;
            for (var x = 0; x < dw; x++)
            {
                var sx = x * sw / dw;
                var si = (sy * sw + sx) * 4;
                var di = (y * dw + x) * 4;
                dest[di] = src[si];
                dest[di + 1] = src[si + 1];
                dest[di + 2] = src[si + 2];
                dest[di + 3] = src[si + 3] == 0 ? (byte)255 : src[si + 3];
            }
        }

        return dest;
    }

    private static byte[] Premultiply(byte[] bgra)
    {
        var copy = (byte[])bgra.Clone();
        for (var i = 0; i < copy.Length; i += 4)
        {
            var a = copy[i + 3] / 255f;
            copy[i] = (byte)(copy[i] * a);
            copy[i + 1] = (byte)(copy[i + 1] * a);
            copy[i + 2] = (byte)(copy[i + 2] * a);
        }

        return copy;
    }
}
