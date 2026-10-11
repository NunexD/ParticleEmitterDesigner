using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ParticleEmitterDesigner.Controls;

/// <summary>
/// A decoded sprite sheet held as a BGRA (straight alpha) byte buffer so the
/// renderer can sample it directly without touching WPF bitmap objects in the
/// hot render loop.
/// </summary>
public sealed class SpriteSheet
{
    private SpriteSheet(byte[] pixels, int width, int height)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>Loads a PNG/BMP/JPG/GIF image. Returns null on failure.</summary>
    public static SpriteSheet? LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(path);
            source.EndInit();

            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var pixels = new byte[width * height * 4];
            bgra.CopyPixels(pixels, width * 4, 0);
            return new SpriteSheet(pixels, width, height);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds an animated 4x4 fallback sheet (soft white blobs whose size and
    /// position vary per frame) so the sprite pipeline is visible even before a
    /// real texture is loaded. Particles tint it via their start/end colour.
    /// </summary>
    public static SpriteSheet CreateProcedural()
    {
        const int cols = 4, rows = 4, cell = 64;
        const int frames = cols * rows;
        int width = cols * cell;
        int height = rows * cell;
        var pixels = new byte[width * height * 4];

        for (int f = 0; f < frames; f++)
        {
            int col = f % cols;
            int row = f / cols;
            float phase = f;
            float cx = cell * 0.5f + 6f * MathF.Sin(phase * 0.9f);
            float cy = cell * 0.5f + 6f * MathF.Cos(phase * 0.7f);
            float radius = 14f + 10f * MathF.Sin(phase * 0.55f);

            int cellX = col * cell;
            int cellY = row * cell;

            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    float dx = (x + 0.5f - cx) / radius;
                    float dy = (y + 0.5f - cy) / radius;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= 1f) continue;

                    float v = 1f - MathF.Sqrt(d2);
                    v *= v;
                    int a = (int)(v * 255f + 0.5f);

                    int i = ((cellY + y) * width + (cellX + x)) * 4;
                    pixels[i] = 255;
                    pixels[i + 1] = 255;
                    pixels[i + 2] = 255;
                    pixels[i + 3] = (byte)a;
                }
            }
        }

        return new SpriteSheet(pixels, width, height);
    }
}