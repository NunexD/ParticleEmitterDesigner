namespace ParticleEmitterDesigner.Models;

/// <summary>Determines where particles spawn relative to the emitter origin
/// and how their initial velocity direction is derived.</summary>
public enum EmitterShape
{
    /// <summary>All particles spawn at the single origin point.</summary>
    Point = 0,

    /// <summary>Particles spawn at a random position inside a filled circle.</summary>
    CircleArea = 1,

    /// <summary>Particles spawn exactly on the rim of a circle.</summary>
    CircleEdge = 2,

    /// <summary>Particles spawn at a random position inside a filled box.</summary>
    Box = 3,

    /// <summary>Particles spawn inside a cone whose apex sits at the origin and
    /// whose axis points along the configured direction.</summary>
    Cone = 4
}