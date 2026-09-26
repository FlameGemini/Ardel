using System.Numerics;
using SkiaSharp;

namespace MinecraftSkinRender.Image;

public static class Skin3DHeadTypeA
{
    private static readonly SKPoint3[] s_cubeVertices =
    [
        // Front face
        new SKPoint3(-1, -1, 1),
        new SKPoint3(1, -1, 1),
        new SKPoint3(1, 1, 1),
        new SKPoint3(-1, 1, 1),
        // Back face
        new SKPoint3(-1, -1, -1),
        new SKPoint3(1, -1, -1),
        new SKPoint3(1, 1, -1),
        new SKPoint3(-1, 1, -1),
        // Front face (Top layer, 1.125x scale)
        new SKPoint3(-1.125f, -1.125f, 1.125f),
        new SKPoint3(1.125f, -1.125f, 1.125f),
        new SKPoint3(1.125f, 1.125f, 1.125f),
        new SKPoint3(-1.125f, 1.125f, 1.125f),
        // Back face (Top layer)
        new SKPoint3(-1.125f, -1.125f, -1.125f),
        new SKPoint3(1.125f, -1.125f, -1.125f),
        new SKPoint3(1.125f, 1.125f, -1.125f),
        new SKPoint3(-1.125f, 1.125f, -1.125f)
    ];

    private static readonly ushort[] s_cubeIndices =
    [
        8, 12, 15, 11, // Back face (Top)
        8, 12, 13, 9, // Bottom face (Top)
        8, 9, 10, 11, // Right face (Top)
        0, 4, 7, 3, // Back face
        0, 4, 5, 1, // Bottom face
        0, 1, 2, 3, // Right face
        3, 7, 6, 2, // Top face
        4, 5, 6, 7, // Left face
        1, 5, 6, 2, // Front face
        11, 15, 14, 10, // Top face (Top)
        12, 13, 14, 15, // Left face (Top)
        9, 13, 14, 10, // Front face (Top)
    ];

    private static readonly SKRectI[] s_facePos =
    [
        new SKRectI(56, 8, 64, 16), // Back face (Top)
        new SKRectI(48, 0, 56, 8), // Bottom face (Top)
        new SKRectI(48, 8, 56, 16), // Right face (Top)
        new SKRectI(24, 8, 32, 16), // Back face
        new SKRectI(16, 0, 24, 8), // Bottom face
        new SKRectI(16, 8, 24, 16), // Right face
        new SKRectI(8, 0, 16, 8), // Top face
        new SKRectI(0, 8, 8, 16), // Left face
        new SKRectI(8, 8, 16, 16), // Front face
        new SKRectI(40, 0, 48, 8), // Top face (Top)
        new SKRectI(32, 8, 40, 16), // Left face (Top)
        new SKRectI(40, 8, 48, 16), // Front face (Top)
    ];

    private static readonly SKPoint[] s_sourceVertices =
    [
        new SKPoint(0, 1), new SKPoint(1, 1), new SKPoint(1, 0), new SKPoint(0, 0), // Back
        new SKPoint(1, 0), new SKPoint(0, 0), new SKPoint(0, 1), new SKPoint(1, 1), // Bottom
        new SKPoint(1, 1), new SKPoint(0, 1), new SKPoint(0, 0), new SKPoint(1, 0), // Right
        new SKPoint(0, 1), new SKPoint(1, 1), new SKPoint(1, 0), new SKPoint(0, 0), // Back
        new SKPoint(1, 0), new SKPoint(0, 0), new SKPoint(0, 1), new SKPoint(1, 1), // Bottom
        new SKPoint(1, 1), new SKPoint(0, 1), new SKPoint(0, 0), new SKPoint(1, 0), // Right
        new SKPoint(1, 0), new SKPoint(0, 0), new SKPoint(0, 1), new SKPoint(1, 1), // Top
        new SKPoint(0, 1), new SKPoint(1, 1), new SKPoint(1, 0), new SKPoint(0, 0), // Left
        new SKPoint(1, 1), new SKPoint(0, 1), new SKPoint(0, 0), new SKPoint(1, 0), // Front
        new SKPoint(1, 0), new SKPoint(0, 0), new SKPoint(0, 1), new SKPoint(1, 1), // Top
        new SKPoint(0, 1), new SKPoint(1, 1), new SKPoint(1, 0), new SKPoint(0, 0), // Left
        new SKPoint(1, 1), new SKPoint(0, 1), new SKPoint(0, 0), new SKPoint(1, 0), // Front
    ];

    public static SKImage MakeHeadImage(SKBitmap skin)
    {
        int width = 400, height = 400;
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        DrawHead3D(canvas, skin);
        return surface.Snapshot();
    }

    private static void DrawHead3D(SKCanvas canvas, SKBitmap texture)
    {
        var transform = CreateTransformMatrix();
        int faceCount = s_cubeIndices.Length / 4;
        for (int i = 0; i < faceCount; i++)
            DrawTexturedFace(canvas, texture, transform, i);
    }

    private static Matrix4x4 CreateTransformMatrix()
    {
        // Build transforms in logical order, then combine with standard multiplication.
        // All transforms are plain Matrix4x4 — no SKMatrix44 confusion.

        // Rotate around Y then X (Minecraft-style isometric look)
        var rotY = Matrix4x4.CreateRotationY(45f * MathF.PI / 180f);
        var rotX = Matrix4x4.CreateRotationX(-30f * MathF.PI / 180f);

        // Flip Y axis (screen Y is down, 3D Y is up) and scale to fit canvas
        var scale = Matrix4x4.CreateScale(100f, -100f, 100f);

        // Translate to center of 400x400 canvas
        var translate = Matrix4x4.CreateTranslation(200f, 200f, 0f);

        // Combine: first rotate Y, then X, then scale, then translate
        // Matrix4x4 uses row-vector convention: result = v * M1 * M2 * ...
        return rotY * rotX * scale * translate;
    }

    private static SKPoint Project(Matrix4x4 mat, SKPoint3 v)
    {
        // Vector4.Transform computes v * M (row-vector convention), which matches Matrix4x4
        var result = Vector4.Transform(new Vector4(v.X, v.Y, v.Z, 1f), mat);

        // Perspective divide (W will be 1 for affine transforms, but kept for correctness)
        if (result.W != 0f)
        {
            result.X /= result.W;
            result.Y /= result.W;
        }

        return new SKPoint(result.X, result.Y);
    }

    private static void DrawTexturedFace(SKCanvas canvas, SKBitmap texture, Matrix4x4 transform, int index)
    {
        var faceRect = s_facePos[index];

        // Properly copy face pixels into a new bitmap (ExtractSubset only aliases)
        var sourceBitmap = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var faceCanvas = new SKCanvas(sourceBitmap))
        {
            faceCanvas.DrawBitmap(
                texture,
                new SKRect(faceRect.Left, faceRect.Top, faceRect.Right, faceRect.Bottom),
                new SKRect(0, 0, 8, 8));
        }

        int baseIndex = index * 4;
        var vertices = new SKPoint[4];
        var texCoords = new SKPoint[4];

        for (int j = 0; j < 4; j++)
        {
            vertices[j] = Project(transform, s_cubeVertices[s_cubeIndices[baseIndex + j]]);
            texCoords[j] = new SKPoint(
                s_sourceVertices[baseIndex + j].X * 8f,
                s_sourceVertices[baseIndex + j].Y * 8f);
        }

        var skVertices = SKVertices.CreateCopy(SKVertexMode.TriangleFan, vertices, texCoords, null);

        // Shader must outlive DrawVertices
        using var shader = SKShader.CreateBitmap(sourceBitmap, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };

        canvas.DrawVertices(skVertices, SKBlendMode.SrcOver, paint);

        sourceBitmap.Dispose(); // safe — shader has already captured pixel data
    }
}