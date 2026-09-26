using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Minecraft world cover at <c>{worldFolder}/icon.png</c>.
/// The game expects a real PNG (typically 64×64); copying JPG/WebP bytes as
/// <c>icon.png</c> makes the singleplayer list fall back to the gray default.
/// </summary>
public static class SaveIconHelper
{
    public const string FileName = "icon.png";
    public const int MinecraftIconSize = 64;

    public static string? FindPath(string worldFolder)
    {
        if (string.IsNullOrWhiteSpace(worldFolder) || !Directory.Exists(worldFolder))
            return null;

        var iconPath = Path.Combine(worldFolder, FileName);
        return File.Exists(iconPath) ? iconPath : null;
    }

    public static void Clear(string worldFolder)
    {
        if (string.IsNullOrWhiteSpace(worldFolder))
            return;

        var iconPath = Path.Combine(worldFolder, FileName);
        try
        {
            if (File.Exists(iconPath))
                File.Delete(iconPath);
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Center-crops to square, resizes to 64×64, and writes a real PNG as <c>icon.png</c>.
    /// </summary>
    public static async Task<string> SetFromFileAsync(
        string worldFolder,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(sourcePath);

        Directory.CreateDirectory(worldFolder);
        var dest = Path.Combine(worldFolder, FileName);
        var temp = dest + ".tmp";

        try
        {
            // FileStream avoids StorageFile path quirks; ignore EXIF so width/height match pixels.
            await using var fileStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous);
            using var input = fileStream.AsRandomAccessStream();

            var decoder = await BitmapDecoder.CreateAsync(input)
                .AsTask(cancellationToken)
                .ConfigureAwait(true);

            var srcW = (int)decoder.PixelWidth;
            var srcH = (int)decoder.PixelHeight;
            if (srcW <= 0 || srcH <= 0)
                throw new InvalidDataException("Image has zero dimensions.");

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
            var square = CenterCropSquare(src, srcW, srcH);
            var side = Math.Min(srcW, srcH);
            var scaled = ScaleBgra(square, side, side, MinecraftIconSize, MinecraftIconSize);

            using var encoded = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder
                .CreateAsync(BitmapEncoder.PngEncoderId, encoded)
                .AsTask(cancellationToken)
                .ConfigureAwait(true);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                MinecraftIconSize,
                MinecraftIconSize,
                96,
                96,
                scaled);
            await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(true);

            encoded.Seek(0);
            var size = (int)encoded.Size;
            var bytes = new byte[size];
            using (var reader = new DataReader(encoded))
            {
                await reader.LoadAsync((uint)size).AsTask(cancellationToken).ConfigureAwait(true);
                reader.ReadBytes(bytes);
            }

            await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(true);
            File.Move(temp, dest, overwrite: true);
            return dest;
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static byte[] CenterCropSquare(byte[] src, int width, int height)
    {
        var side = Math.Min(width, height);
        var ox = (width - side) / 2;
        var oy = (height - side) / 2;
        var dest = new byte[side * side * 4];
        for (var y = 0; y < side; y++)
        {
            var srcRow = ((oy + y) * width + ox) * 4;
            var dstRow = y * side * 4;
            System.Buffer.BlockCopy(src, srcRow, dest, dstRow, side * 4);
        }

        return dest;
    }

    private static byte[] ScaleBgra(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        if (srcW == dstW && srcH == dstH)
            return src;

        var dest = new byte[dstW * dstH * 4];
        for (var y = 0; y < dstH; y++)
        {
            var sy = Math.Min(srcH - 1, y * srcH / dstH);
            for (var x = 0; x < dstW; x++)
            {
                var sx = Math.Min(srcW - 1, x * srcW / dstW);
                var si = (sy * srcW + sx) * 4;
                var di = (y * dstW + x) * 4;
                dest[di] = src[si];
                dest[di + 1] = src[si + 1];
                dest[di + 2] = src[si + 2];
                dest[di + 3] = src[si + 3] == 0 ? (byte)255 : src[si + 3];
            }
        }

        return dest;
    }

    /// <summary>Minecraft world icons are 64×64; decode square to avoid stretch blur.</summary>
    public static BitmapImage? CreateImage(string? path, int decodePixels = 96)
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
