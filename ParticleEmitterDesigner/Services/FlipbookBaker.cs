using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ParticleEmitterDesigner.Controls;
using ParticleEmitterDesigner.Engine;
using ParticleEmitterDesigner.Models;

namespace ParticleEmitterDesigner.Services;

/// <summary>
/// Bakes a particle effect into a flipbook-style sprite sheet and saves it as a
/// 32-bit PNG with transparency.
///
/// The simulation is stepped deterministically (fresh emitter, fixed random
/// seed, fixed sub-step size) for <c>totalDuration</c> seconds. A snapshot is
/// rasterised into every cell of a power-of-two transparent texture, giving a
/// frame-perfect loop that can be replayed in a game engine.
/// </summary>
public static class FlipbookBaker
{
    public const int TextureSize = 2048;

    private const int BakeSeed = 2024;
    private const int SubStepsPerFrame = 4;

    /// <summary>
    /// Bakes the effect into <paramref name="outputPath"/>. The emitter used for
    /// baking is a separate, deterministic instance so the live viewport is not
    /// disturbed. Throws <see cref="IOException"/> on writing failures.
    /// </summary>
    public static unsafe void Bake(EmitterSettings settings, SpriteSheet? sprite, int gridSize,
        double totalDuration, string outputPath)
    {
        int grid = Math.Clamp(gridSize, 1, TextureSize);
        int cellSize = TextureSize / grid;
        int totalFrames = grid * grid;

        double duration = Math.Max(0.01, totalDuration);
        double frameDuration = duration / totalFrames;
        double subDt = frameDuration / SubStepsPerFrame;

        var pixels = new byte[TextureSize * TextureSize * 4];
        var emitter = new ParticleEmitter(settings, seed: BakeSeed);

        for (int frame = 0; frame < totalFrames; frame++)
        {
            int col = frame % grid;
            int row = frame / grid;

            // Spawn from the centre of the current cell.
            emitter.OriginX = col * cellSize + cellSize / 2f;
            emitter.OriginY = row * cellSize + cellSize / 2f;

            // Advance the deterministic simulation towards this frame.
            for (int step = 0; step < SubStepsPerFrame; step++)
            {
                emitter.Update(subDt);
            }

            int clipLeft = col * cellSize;
            int clipTop = row * cellSize;
            int clipRight = clipLeft + cellSize - 1;
            int clipBottom = clipTop + cellSize - 1;

            fixed (byte* buffer = pixels)
            {
                ParticleRasterizer.Rasterize(
                    buffer, TextureSize, TextureSize, TextureSize * 4,
                    emitter, 1.0, 1.0, 0, 0, sprite,
                    clipLeft, clipTop, clipRight, clipBottom);
            }
        }

        SavePng(pixels, outputPath);
    }

    private static void SavePng(byte[] pixels, string outputPath)
    {
        var bitmap = BitmapSource.Create(
            TextureSize, TextureSize, 96, 96,
            PixelFormats.Bgra32, null, pixels, TextureSize * 4);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }
}