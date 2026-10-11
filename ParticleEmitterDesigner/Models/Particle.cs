namespace ParticleEmitterDesigner.Models;

/// <summary>
/// A single simulation particle. This is a mutable struct so the emitter can
/// keep a single <see cref="Particle"/>[] pool alive for the whole application
/// lifetime (zero steady-state GC allocations).
/// All units are in device-independent pixels / seconds.
/// </summary>
public struct Particle
{
    // Position (device-independent pixels).
    public float X;
    public float Y;

    // Velocity (pixels / second).
    public float VelocityX;
    public float VelocityY;

    // Lifetime tracking (seconds).
    public float Age;
    public float Lifespan;

    // Size interpolation (diameter in pixels).
    public float StartSize;
    public float EndSize;

    // Size-over-lifetime multiplier, interpolated across the particle's life
    // (e.g. 1 -> 0 to shrink out of existence). Multiplied against the
    // interpolated Start/End diameter.
    public float ScaleStart;
    public float ScaleEnd;

    // Colour ramp (non-premultiplied RGBA, 0-255). Sampled across three stops:
    // Start (t = 0), Mid (t = 0.5) and End (t = 1).
    public byte StartR, StartG, StartB, StartA;
    public byte MidR, MidG, MidB, MidA;
    public byte EndR, EndG, EndB, EndA;

    // Sprite-sheet animation: a per-particle start offset (in frame units,
    // randomised at spawn) so particles don't all begin on frame 0.
    public float FrameOffset;

    /// <summary>Normalised life in the range [0, 1].</summary>
    public readonly float LifeRatio => Lifespan > 0f ? Age / Lifespan : 1f;

    /// <summary>Interpolated diameter for the current frame.</summary>
    public readonly float CurrentSize
    {
        get
        {
            float t = LifeRatio;
            return StartSize + (EndSize - StartSize) * t;
        }
    }

    /// <summary>Interpolated size multiplier for the current frame.</summary>
    public readonly float CurrentScale
    {
        get
        {
            float t = LifeRatio;
            return ScaleStart + (ScaleEnd - ScaleStart) * t;
        }
    }

    /// <summary>
    /// Samples the three-stop colour ramp at the normalised age
    /// <paramref name="t"/> in [0, 1]. The first half blends Start -> Mid, the
    /// second half blends Mid -> End, giving a smooth colour *and* alpha
    /// transition over the particle's lifetime. Components are returned
    /// non-premultiplied in the 0-255 range.
    /// </summary>
    public readonly void SampleColor(float t, out float r, out float g, out float b, out float a)
    {
        if (t < 0f) t = 0f;
        else if (t > 1f) t = 1f;

        if (t < 0.5f)
        {
            float k = t * 2f;
            r = StartR + (MidR - StartR) * k;
            g = StartG + (MidG - StartG) * k;
            b = StartB + (MidB - StartB) * k;
            a = StartA + (MidA - StartA) * k;
        }
        else
        {
            float k = (t - 0.5f) * 2f;
            r = MidR + (EndR - MidR) * k;
            g = MidG + (EndG - MidG) * k;
            b = MidB + (EndB - MidB) * k;
            a = MidA + (EndA - MidA) * k;
        }
    }
}
