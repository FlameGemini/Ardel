using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Renders the Ardel mark (C3f: solid flat-top hex + opposite open rings)
/// at an exact pixel size with supersampled anti-aliasing.
/// </summary>
public static class ArdelLogoRenderer
{
    private const float DesignSize = 128f;
    private const float HexR = 48f;
    private const float OuterIr = 32f;
    private const float OuterStroke = 7f;
    private const float OuterPad = 0.12f;
    private const float InnerIr = 12f;
    private const float InnerStroke = 6f;
    private const float InnerPad = 0.12f;

    public static async Task<BitmapImage> CreateAsync(
        int pixelSize,
        bool lightShell,
        CancellationToken cancellationToken = default)
    {
        pixelSize = Math.Clamp(pixelSize, 64, 1024);
        var ink = lightShell
            ? new SKColor(28, 32, 36, 255)
            : SKColors.White;

        var png = await Task.Run(() => RenderPng(pixelSize, ink), cancellationToken)
            .ConfigureAwait(true);
        return await Skin3DHeadHelper.BitmapImageFromPngAsync(png, cancellationToken)
            .ConfigureAwait(true);
    }

    private static byte[] RenderPng(int size, SKColor ink)
    {
        // Match Assets/_render_ardel_logo.py: draw hard at 4× then downscale for AA.
        const int ss = 4;
        var big = size * ss;
        var scale = big / DesignSize;
        var cx = 64f * scale;
        var cy = 64f * scale;
        var r = HexR * scale;

        using var hiBmp = new SKBitmap(big, big, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(hiBmp))
        {
            canvas.Clear(SKColors.Transparent);

            using var fill = new SKPaint
            {
                IsAntialias = false,
                Style = SKPaintStyle.Fill,
                Color = ink
            };

            using (var hex = BuildHexPath(cx, cy, r))
                canvas.DrawPath(hex, fill);

            using var cutPaint = new SKPaint
            {
                IsAntialias = false,
                Style = SKPaintStyle.Fill,
                BlendMode = SKBlendMode.DstOut,
                Color = SKColors.Black
            };

            using (var outer = BuildOpenRingCut(cx, cy, OuterIr * scale, OuterStroke * scale, (0, 1), OuterPad))
                canvas.DrawPath(outer, cutPaint);
            using (var inner = BuildOpenRingCut(cx, cy, InnerIr * scale, InnerStroke * scale, (3, 4), InnerPad))
                canvas.DrawPath(inner, cutPaint);
        }

        using var loBmp = hiBmp.Resize(
            new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var image = SKImage.FromBitmap(loBmp ?? hiBmp);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray() ?? Array.Empty<byte>();
    }

    private static SKPath BuildHexPath(float cx, float cy, float r)
    {
        var pts = HexPoints(cx, cy, r);
        var path = new SKPath();
        path.MoveTo(pts[0]);
        for (var i = 1; i < 6; i++)
            path.LineTo(pts[i]);
        path.Close();
        return path;
    }

    /// <summary>Open hex ring = (outer hex − inner hex) − gap wedge. Same as Pillow <c>_open_ring</c>.</summary>
    private static SKPath BuildOpenRingCut(
        float cx,
        float cy,
        float ir,
        float stroke,
        (int I0, int I1) openVerts,
        float pad)
    {
        var outerR = ir + stroke / 2f;
        var innerR = MathF.Max(1f, ir - stroke / 2f);
        var outer = HexPoints(cx, cy, outerR);
        var inner = HexPoints(cx, cy, innerR);

        using var ring = new SKPath { FillType = SKPathFillType.EvenOdd };
        ring.MoveTo(outer[0]);
        for (var i = 1; i < 6; i++)
            ring.LineTo(outer[i]);
        ring.Close();
        ring.MoveTo(inner[0]);
        for (var i = 1; i < 6; i++)
            ring.LineTo(inner[i]);
        ring.Close();

        using var gap = new SKPath();
        var i0 = openVerts.I0;
        var i1 = openVerts.I1;
        var a0 = MathF.Atan2(outer[i0].Y - cy, outer[i0].X - cx) - pad;
        var a1 = MathF.Atan2(outer[i1].Y - cy, outer[i1].X - cx) + pad;
        var diff = a1 - a0;
        while (diff > MathF.PI)
            diff -= MathF.PI * 2f;
        while (diff < -MathF.PI)
            diff += MathF.PI * 2f;

        gap.MoveTo(cx, cy);
        const int steps = 24;
        var reach = ir + stroke * 3.2f;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var ang = a0 + diff * t;
            gap.LineTo(cx + MathF.Cos(ang) * reach, cy + MathF.Sin(ang) * reach);
        }

        gap.Close();

        return ring.Op(gap, SKPathOp.Difference) ?? new SKPath(ring);
    }

    private static SKPoint[] HexPoints(float cx, float cy, float r)
    {
        var pts = new SKPoint[6];
        for (var i = 0; i < 6; i++)
        {
            var a = i * (MathF.PI / 3f);
            pts[i] = new SKPoint(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
        }

        return pts;
    }
}
