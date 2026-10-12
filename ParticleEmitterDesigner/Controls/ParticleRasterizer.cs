using ParticleEmitterDesigner.Engine;
using ParticleEmitterDesigner.Models;

namespace ParticleEmitterDesigner.Controls;

/// <summary>
/// Software rasteriser shared by the live viewport and the sprite-sheet baker.
/// Composites every particle into a caller-owned BGRA buffer using either
/// additive or straight-alpha blending, per the <see cref="EmitterSettings.BlendMode"/>.
///
/// The buffer is expected to have been cleared by the caller (opaque background
/// for the viewport, transparent for baking) before the call. The rasteriser
/// supports sprite-sheet flipbook animation, per-particle rotation and a radial
/// soft-edge falloff. All coordinate math happens in the buffer's pixel space
/// (<paramref name="scaleX"/>/<paramref name="scaleY"/> convert the emitter's
/// DIP units into target pixels).
/// </summary>
public static class ParticleRasterizer
{
    private const int FalloffResolution = 256;

    // Radial falloff sampled by *squared* normalised distance to avoid a sqrt
    // per pixel. Index 0 = centre (full intensity), last index = edge (~0).
    private static readonly byte[] Falloff = BuildFalloff(FalloffResolution);

    private static readonly byte[] SingleWhite = { 255, 255, 255, 255 };

    public static unsafe void Rasterize(
        byte* buffer, int width, int height, int stride,
        ParticleEmitter emitter, double scaleX, double scaleY,
        double offsetX, double offsetY,
        SpriteSheet? sprite,
        int clipLeft, int clipTop, int clipRight, int clipBottom)
    {
        EmitterSettings settings = emitter.Settings;
        Particle[] particles = emitter.Particles;
        int count = emitter.ActiveCount;

        float sx = (float)scaleX;
        float sy = (float)scaleY;
        float ox = (float)offsetX;
        float oy = (float)offsetY;
        int falloffMax = Falloff.Length - 1;

        SpriteSheet? sheet = sprite;

        // Resolve the particle fill mode: 0 = solid circle, 1 = soft glow,
        // 2 = custom texture. A CustomTexture request falls back to the soft
        // glow when no sheet has been loaded yet.
        int appearance = settings.TextureMode switch
        {
            TextureMode.CustomTexture when sheet != null => 2,
            TextureMode.ProceduralCircle => 0,
            _ => 1,
        };
        bool sampleTexture = appearance == 2;

        // Derive the animation grid from the requested frame count.
        int frames = 1, cols = 1, rows = 1, cellW = 0, cellH = 0;
        if (sampleTexture)
        {
            double fc = settings.FrameCount;
            if (fc < 1.0) fc = 1.0;
            else if (fc > 4096.0) fc = 4096.0;

            frames = (int)Math.Floor(fc + 1e-9);
            if (frames > 1)
            {
                cols = (int)Math.Ceiling(Math.Sqrt(frames));
                rows = cols; // square grid, e.g. 4x4 for 16 frames
                cellW = sheet!.Width / cols;
                cellH = sheet.Height / rows;
                if (cellW < 1) cellW = 1;
                if (cellH < 1) cellH = 1;
            }
        }

        float animSpeed = (float)Math.Clamp(settings.AnimationSpeed, 0.0, 1000.0);
        bool additive = settings.BlendMode == BlendMode.Additive;

        fixed (byte* spritePtr = sampleTexture ? sheet!.Pixels : SingleWhite)
        {
            int sw = sampleTexture ? sheet!.Width : 1;
            int sh = sampleTexture ? sheet!.Height : 1;

            for (int i = 0; i < count; i++)
            {
                ref Particle p = ref particles[i];

                float t = p.Lifespan > 0f ? p.Age / p.Lifespan : 1f;
                if (t < 0f) t = 0f;
                else if (t > 1f) t = 1f;

                float cx = p.X * sx + ox;
                float cy = p.Y * sy + oy;
                float radius = p.CurrentSize * p.CurrentScale * 0.5f * sx;
                if (radius <= 0f) continue;
                if (radius < 0.5f) radius = 0.5f;

                // Sample the three-stop colour ramp (Start -> Mid -> End) at the
                // particle's normalised age. Components are non-premultiplied.
                p.SampleColor(t, out float fr, out float fg, out float fb, out float fa);
                if (fa <= 0f) continue;
                float particleAlpha = fa * (1f / 255f);

                int minX = (int)(cx - radius); if (minX < clipLeft) minX = clipLeft;
                int maxX = (int)(cx + radius + 0.999f); if (maxX > clipRight) maxX = clipRight;
                int minY = (int)(cy - radius); if (minY < clipTop) minY = clipTop;
                int maxY = (int)(cy + radius + 0.999f); if (maxY > clipBottom) maxY = clipBottom;
                if (minX > maxX || minY > maxY) continue;

                // Which animation frame is this particle showing right now?
                int colOff = 0, rowOff = 0;
                if (sampleTexture && frames > 1)
                {
                    // Animation cycles over the particle's lifespan; frames loop.
                    float fp = t * frames * animSpeed + p.FrameOffset;
                    int fi = (int)fp;
                    int frame = fi % frames;
                    if (frame < 0) frame += frames;
                    colOff = frame % cols;
                    rowOff = frame / cols;
                }

                int cellOriginX = colOff * cellW;
                int cellOriginY = rowOff * cellH;

                // Per-particle rotation, advanced by the emitter each frame.
                float rotCos = 1f, rotSin = 0f;
                if (sampleTexture && p.Rotation != 0f)
                {
                    rotCos = MathF.Cos(p.Rotation);
                    rotSin = MathF.Sin(p.Rotation);
                }

                float invR = 1f / radius;

                for (int y = minY; y <= maxY; y++)
                {
                    float dy = (y + 0.5f - cy) * invR;
                    float dy2 = dy * dy;
                    byte* pxo = buffer + y * stride + minX * 4;

                    for (int x = minX; x <= maxX; x++, pxo += 4)
                    {
                        float dx = (x + 0.5f - cx) * invR;
                        float d2 = dx * dx + dy2;
                        if (d2 >= 1f) continue;

                        int fragB, fragG, fragR, coverage;

                        if (sampleTexture)
                        {
                            float lx, ly;
                            if (rotCos != 1f || rotSin != 0f)
                            {
                                lx = dx * rotCos + dy * rotSin;
                                ly = -dx * rotSin + dy * rotCos;
                            }
                            else
                            {
                                lx = dx;
                                ly = dy;
                            }
                            if (lx * lx >= 1f || ly * ly >= 1f) continue;

                            float u = lx * 0.5f + 0.5f;
                            float v = ly * 0.5f + 0.5f;
                            if (u >= 1f) u = 0.9999f;
                            if (v >= 1f) v = 0.9999f;

                            float tu = cellOriginX + u * cellW;
                            float tv = cellOriginY + v * cellH;

                            int x0 = (int)tu; if (x0 > cellOriginX + cellW - 1) x0 = cellOriginX + cellW - 1;
                            int y0 = (int)tv; if (y0 > cellOriginY + cellH - 1) y0 = cellOriginY + cellH - 1;
                            int x1 = x0 + 1 <= cellOriginX + cellW - 1 ? x0 + 1 : x0;
                            int y1 = y0 + 1 <= cellOriginY + cellH - 1 ? y0 + 1 : y0;

                            float fx = tu - x0;
                            float fy = tv - y0;

                            int i00 = (y0 * sw + x0) * 4;
                            int i10 = (y0 * sw + x1) * 4;
                            int i01 = (y1 * sw + x0) * 4;
                            int i11 = (y1 * sw + x1) * 4;

                            float sb0 = spritePtr[i00] + (spritePtr[i10] - spritePtr[i00]) * fx;
                            float sb1 = spritePtr[i01] + (spritePtr[i11] - spritePtr[i01]) * fx;
                            float sg0 = spritePtr[i00 + 1] + (spritePtr[i10 + 1] - spritePtr[i00 + 1]) * fx;
                            float sg1 = spritePtr[i01 + 1] + (spritePtr[i11 + 1] - spritePtr[i01 + 1]) * fx;
                            float sr0 = spritePtr[i00 + 2] + (spritePtr[i10 + 2] - spritePtr[i00 + 2]) * fx;
                            float sr1 = spritePtr[i01 + 2] + (spritePtr[i11 + 2] - spritePtr[i01 + 2]) * fx;
                            float sa0 = spritePtr[i00 + 3] + (spritePtr[i10 + 3] - spritePtr[i00 + 3]) * fx;
                            float sa1 = spritePtr[i01 + 3] + (spritePtr[i11 + 3] - spritePtr[i01 + 3]) * fx;

                            int sb = (int)(sb0 + (sb1 - sb0) * fy);
                            int sg = (int)(sg0 + (sg1 - sg0) * fy);
                            int sr = (int)(sr0 + (sr1 - sr0) * fy);
                            int sa = (int)(sa0 + (sa1 - sa0) * fy);

                            // Tint the (non-premultiplied) ramp colour by the
                            // sprite colour; the sprite's own alpha provides the
                            // coverage (so the texture's silhouette is honoured).
                            fragB = (int)(fb * sb / 255f);
                            fragG = (int)(fg * sg / 255f);
                            fragR = (int)(fr * sr / 255f);
                            coverage = sa;
                        }
                        else
                        {
                            // Procedural blob: fragment colour = ramp colour,
                            // coverage from the fill mode.
                            int fade;
                            if (appearance == 0)
                            {
                                // Solid disc with a ~1 px antialiased rim. In
                                // squared-distance space the rim is roughly
                                // 2 / radius wide, hence the radius * 0.5 term.
                                fade = (int)((1f - d2) * radius * 0.5f * 255f);
                                if (fade > 255) fade = 255;
                            }
                            else
                            {
                                fade = Falloff[(int)(d2 * falloffMax)];
                            }

                            if (fade <= 0) continue;

                            fragB = (int)fb;
                            fragG = (int)fg;
                            fragR = (int)fr;
                            coverage = fade;
                        }

                        if (coverage == 0) continue;

                        if (additive)
                        {
                            // Saturated additive: dst.rgb += src.rgb (clamped to
                            // 255). The particle's ramp alpha acts as intensity.
                            int b = pxo[0] + (int)(fragB * particleAlpha * coverage / 255f); if (b > 255) b = 255;
                            int g = pxo[1] + (int)(fragG * particleAlpha * coverage / 255f); if (g > 255) g = 255;
                            int r = pxo[2] + (int)(fragR * particleAlpha * coverage / 255f); if (r > 255) r = 255;

                            int a = pxo[3] + coverage; if (a > 255) a = 255;

                            pxo[0] = (byte)b;
                            pxo[1] = (byte)g;
                            pxo[2] = (byte)r;
                            pxo[3] = (byte)a;
                        }
                        else
                        {
                            // Standard source-over: out = src*a + dst*(1-a),
                            // where the source alpha combines the ramp alpha and
                            // the geometric coverage.
                            int aSrc = (int)(particleAlpha * coverage + 0.5f);
                            int inv = 255 - aSrc;
                            pxo[0] = (byte)((fragB * aSrc + pxo[0] * inv) / 255);
                            pxo[1] = (byte)((fragG * aSrc + pxo[1] * inv) / 255);
                            pxo[2] = (byte)((fragR * aSrc + pxo[2] * inv) / 255);

                            int a0 = pxo[3];
                            int a = aSrc + a0 * (255 - aSrc) / 255;
                            pxo[3] = (byte)a;
                        }
                    }
                }
            }
        }
    }

    private static byte[] BuildFalloff(int resolution)
    {
        var lut = new byte[resolution];
        int max = resolution - 1;
        for (int i = 0; i < resolution; i++)
        {
            double d2 = (double)i / max;          // normalised squared distance
            double d = Math.Sqrt(d2);             // normalised distance
            double v = 1.0 - d;                   // linear ramp to the edge
            v = v * v;                            // soft quadratic falloff
            int b = (int)(v * 255.0 + 0.5);
            lut[i] = (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
        }
        return lut;
    }
}