using SkiaSharp;

namespace MinecraftSkinRender.Image;

/// <summary>
/// 3D头像生成，生成一个可以旋转的
/// </summary>
public static class Skin3DHeadTypeB
{
    private const int s_size = 220;
    private const int s_cubeSize = 60;
    private const int s_cubeSizeOut = s_cubeSize + (int)(s_cubeSize * 0.125);
    // Define cube vertices
    private static readonly SKPoint3[] s_cubeVertices =
    [
        //In
        new SKPoint3(-s_cubeSize, -s_cubeSize, -s_cubeSize),
        new SKPoint3(s_cubeSize, -s_cubeSize, -s_cubeSize),
        new SKPoint3(s_cubeSize, s_cubeSize, -s_cubeSize),
        new SKPoint3(-s_cubeSize, s_cubeSize, -s_cubeSize),
        new SKPoint3(-s_cubeSize, -s_cubeSize, s_cubeSize),
        new SKPoint3(s_cubeSize, -s_cubeSize, s_cubeSize),
        new SKPoint3(s_cubeSize, s_cubeSize, s_cubeSize),
        new SKPoint3(-s_cubeSize, s_cubeSize, s_cubeSize),
        //Out
        new SKPoint3(-s_cubeSizeOut, -s_cubeSizeOut, -s_cubeSizeOut),
        new SKPoint3(s_cubeSizeOut, -s_cubeSizeOut, -s_cubeSizeOut),
        new SKPoint3(s_cubeSizeOut, s_cubeSizeOut, -s_cubeSizeOut),
        new SKPoint3(-s_cubeSizeOut, s_cubeSizeOut, -s_cubeSizeOut),
        new SKPoint3(-s_cubeSizeOut, -s_cubeSizeOut, s_cubeSizeOut),
        new SKPoint3(s_cubeSizeOut, -s_cubeSizeOut, s_cubeSizeOut),
        new SKPoint3(s_cubeSizeOut, s_cubeSizeOut, s_cubeSizeOut),
        new SKPoint3(-s_cubeSizeOut, s_cubeSizeOut, s_cubeSizeOut),
    ];

    // Define the faces of the cube (each face is a list of vertex indices)
    private static readonly int[][] s_faces =
    [
        [8, 11, 15, 12],  // Back face (Top)
        [10, 11, 15, 6],  // Bottom face (Top)
        [12, 13, 14, 15], // Right face (Top)
        [0, 3, 7, 4],     // Back face
        [2, 3, 7, 6],     // Bottom face
        [4, 5, 6, 7],     // Right face
        [0, 1, 5, 4],     // Top face
        [0, 1, 2, 3],     // Left face
        [1, 2, 6, 5],     // Front face
        [8, 9, 13, 12],   // Top face (Top)
        [8, 9, 10, 11],   // Left face (Top)
        [9, 10, 14, 13],  // Front face (Top)
    ];

    // Define the colors for each face
    private static readonly SKRectI[] s_facePos =
    [
        new SKRectI(56, 8, 64, 16), // Back face (Top)
        new SKRectI(48, 0, 56, 8),  // Bottom face (Top)
        new SKRectI(48, 8, 56, 16), // Right face (Top)
        new SKRectI(24, 8, 32, 16), // Back face
        new SKRectI(16, 0, 24, 8),  // Bottom face
        new SKRectI(16, 8, 24, 16), // Right face
        new SKRectI(8, 0, 16, 8),   // Top face
        new SKRectI(0, 8, 8, 16),   // Left face
        new SKRectI(8, 8, 16, 16),  // Front face
        new SKRectI(40, 0, 48, 8),  // Top face (Top)
        new SKRectI(32, 8, 40, 16), // Left face (Top)
        new SKRectI(40, 8, 48, 16), // Front face (Top)
    ];

    // 定义原始图像的四个顶点
    private static readonly SKPoint[][] s_sourceVertices =
    [
        [
            // Back face
            new SKPoint(8, 0),
            new SKPoint(8, 8),
            new SKPoint(0, 8),
            new SKPoint(0, 0)
        ],
        [
            // Bottom face
            new SKPoint(0, 8),
            new SKPoint(0, 0),
            new SKPoint(8, 0),
            new SKPoint(8, 8)
        ],
        [
            // Right face
            new SKPoint(8, 0),
            new SKPoint(0, 0),
            new SKPoint(0, 8),
            new SKPoint(8, 8)
        ],
        [
            // Back face
            new SKPoint(8, 0),
            new SKPoint(8, 8),
            new SKPoint(0, 8),
            new SKPoint(0, 0)
        ],
        [
            // Bottom face
            new SKPoint(0, 8),
            new SKPoint(0, 0),
            new SKPoint(8, 0),
            new SKPoint(8, 8)
        ],
        [
            // Right face
            new SKPoint(8, 0),
            new SKPoint(0, 0),
            new SKPoint(0, 8),
            new SKPoint(8, 8)
        ],
        [
            // Top face
            new SKPoint(0, 0),
            new SKPoint(0, 8),
            new SKPoint(8, 8),
            new SKPoint(8, 0)
        ],
        [
            // Left face
            new SKPoint(0, 0),
            new SKPoint(8, 0),
            new SKPoint(8, 8),
            new SKPoint(0, 8)
        ],
        [
            // Front face
            new SKPoint(0, 0),
            new SKPoint(0, 8),
            new SKPoint(8, 8),
            new SKPoint(8, 0)
        ],
        [
            // Top face
            new SKPoint(0, 0),
            new SKPoint(0, 8),
            new SKPoint(8, 8),
            new SKPoint(8, 0)
        ],
        [
            // Left face
            new SKPoint(0, 0),
            new SKPoint(8, 0),
            new SKPoint(8, 8),
            new SKPoint(0, 8)
        ],
        [
            // Front face
            new SKPoint(0, 0),
            new SKPoint(0, 8),
            new SKPoint(8, 8),
            new SKPoint(8, 0)
        ]
    ];

    /// <summary>
    /// 生成一张皮肤头像
    /// </summary>
    /// <param name="skin">皮肤</param>
    /// <param name="x">x轴旋转</param>
    /// <param name="y">y轴旋转</param>
    /// <returns></returns>
    public static SKImage MakeHeadImage(SKBitmap skin, int x, int y)
    {
        // 创建画布
        var info = new SKImageInfo(s_size, s_size);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // 绘制头部
        DrawHead3D(canvas, skin, x, y);

        // 保存结果到文件
        return surface.Snapshot();
    }

    // Translate to center
    private static SKPoint TranslateToCenter(SKPoint3 point3)
    {
        return new SKPoint(point3.X + s_size / 2, point3.Y + s_size / 2);
    }

    private static void DrawHead3D(SKCanvas canvas, SKBitmap skin, int x, int y)
    {
        // Define transformation matrices
        var rotationX = SKMatrix44.CreateRotationDegrees(1, 0, 0, x);
        var rotationY = SKMatrix44.CreateRotationDegrees(0, 1, 0, y);
        var pos = SKMatrix44.CreateTranslation(0, -6f, 5f);
        var rotation = SKMatrix44.CreateIdentity();

        rotation = rotation.PreConcat(rotationY);
        rotation = rotation.PreConcat(rotationX);
        rotation = rotation.PostConcat(pos);

        var sKPoints = new SKPoint3[s_cubeVertices.Length];

        // Apply transformations
        for (int i = 0; i < s_cubeVertices.Length; i++)
        {
            float[] vector = [s_cubeVertices[i].X, s_cubeVertices[i].Y, s_cubeVertices[i].Z, 1];
            var res = ImageHelper.MapScalars(rotation, vector);
            sKPoints[i] = new SKPoint3(res[0], res[1], res[2]);
        }

        // Apply perspective manually
        float perspectiveZ = 0.001f;
        for (int i = 0; i < sKPoints.Length; i++)
        {
            float z = sKPoints[i].Z * perspectiveZ + 1;
            sKPoints[i].X /= z;
            sKPoints[i].Y /= z;
        }

        {
            // Define the shadow offset and blur radius
            float offsetX = 0;
            float offsetY = 0;
            float blurRadius = 20;

            // Define the ellipse
            var ellipseRect = new SKRect(20, 140, 200, 180);

            // Create the paint for the shadow
            using var shadowPaint = new SKPaint();
            shadowPaint.IsAntialias = true;
            shadowPaint.Color = new SKColor(0, 0, 0, 128); // Semi-transparent black for shadow
            shadowPaint.ImageFilter = SKImageFilter.CreateBlur(blurRadius, blurRadius);

            // Create a shadow ellipse rect
            SKRect shadowRect = ellipseRect;
            shadowRect.Offset(offsetX, offsetY);

            // Draw the shadow (an ellipse)
            canvas.DrawOval(shadowRect, shadowPaint);
        }

        for (int index = 0; index < s_faces.Length; index++)
        {
            var face = s_faces[index];

            // 将3D顶点投影到2D平面上
            var projectedVertices = new SKPoint[]
            {
                TranslateToCenter(sKPoints[face[0]]),
                TranslateToCenter(sKPoints[face[1]]),
                TranslateToCenter(sKPoints[face[2]]),
                TranslateToCenter(sKPoints[face[3]])
            };

            using var sourceBitmap = new SKBitmap(8, 8);
            skin.ExtractSubset(sourceBitmap, s_facePos[index]);

            var skVertices = SKVertices.CreateCopy(SKVertexMode.TriangleFan, projectedVertices, s_sourceVertices[index], null);

            // Shader must outlive DrawVertices
            using var shader = SKShader.CreateBitmap(sourceBitmap, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { IsAntialias = true, Shader = shader };

            canvas.DrawVertices(skVertices, SKBlendMode.SrcOver, paint);
        }
    }
}