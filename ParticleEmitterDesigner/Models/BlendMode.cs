namespace ParticleEmitterDesigner.Models;

/// <summary>How overlapping particles are composited against what is already on screen.</summary>
public enum BlendMode
{
    /// <summary>Standard straight-alpha compositing; particles occlude each other.</summary>
    AlphaBlend = 0,

    /// <summary>
    /// Additive blending: each particle's RGB is added to the destination,
    /// clamped to 255, so overlapping particles produce hot glowing cores.
    /// </summary>
    Additive = 1
}