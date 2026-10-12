namespace ParticleEmitterDesigner.Models;

/// <summary>
/// Selects how each particle is filled. The two procedural modes need no asset
/// and always tint by the colour ramp; <see cref="CustomTexture"/> samples a
/// loaded sprite sheet (optionally animated) and tints it with the ramp.
/// </summary>
public enum TextureMode
{
    /// <summary>Solid disc with a soft one-pixel edge.</summary>
    ProceduralCircle = 0,

    /// <summary>Radial falloff, alpha = (1 - r / R)^2, giving a soft glow.</summary>
    ProceduralSoftGlow = 1,

    /// <summary>Loads the texture named by <see cref="EmitterSettings.TexturePath"/>
    /// and samples it per particle (flipbook animation + rotation aware).</summary>
    CustomTexture = 2
}
