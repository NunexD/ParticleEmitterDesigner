namespace ParticleEmitterDesigner.Models;

/// <summary>Determines where particles spawn relative to the emitter origin
/// and how their initial velocity direction is derived.</summary>
public enum EmissionShapeType
{
    /// <summary>All particles spawn at the single origin point.</summary>
    Point = 0,

    /// <summary>Particles spawn uniformly inside a filled circle of
    /// <see cref="EmitterSettings.ShapeRadius"/>.</summary>
    Circle = 1,

    /// <summary>2D ring / perimeter: particles spawn exactly on a circle of
    /// radius <see cref="EmitterSettings.ShapeRadius"/>.</summary>
    Sphere = 2,

    /// <summary>Particles spawn uniformly inside an axis-aligned box defined by
    /// <see cref="EmitterSettings.ShapeBoxExtentsX"/> / <c>ShapeBoxExtentsY</c>.</summary>
    Box = 3,

    /// <summary>Particles spawn inside a cone whose apex sits at the origin and
    /// whose axis points along the configured direction.</summary>
    Cone = 4
}
