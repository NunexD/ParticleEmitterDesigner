using ParticleEmitterDesigner.Models;

namespace ParticleEmitterDesigner.Services;

/// <summary>A named, lazily-built built-in preset.</summary>
public sealed record BuiltInPreset(string Name, Func<EmitterSettings> Create);

/// <summary>
/// Ships a small library of hand-tuned starting points. Each factory returns a
/// fresh <see cref="EmitterSettings"/> so a preset can be applied to any document
/// without sharing mutable state. The presets deliberately exercise the engine's
/// features (emission shapes, radial/tangential acceleration, colour ramps,
/// scale-over-life and the texture modes).
/// </summary>
public static class PresetService
{
    /// <summary>All built-in presets, in menu order.</summary>
    public static IReadOnlyList<BuiltInPreset> All { get; } = new[]
    {
        new BuiltInPreset("Fire / Campfire", CreateFire),
        new BuiltInPreset("Cosmic Vortex", CreateCosmicVortex),
        new BuiltInPreset("Smoke", CreateSmoke),
        new BuiltInPreset("Fountain", CreateFountain),
    };

    /// <summary>Upward flame: additive cone emission, buoyancy and a warm ramp.</summary>
    public static EmitterSettings CreateFire() => new()
    {
        MaxParticles = 1500,
        EmissionRate = 500,
        EmissionShape = EmissionShapeType.Cone,
        ShapeRadius = 60,
        BurstCount = 250,

        LifespanMin = 0.6,
        LifespanMax = 1.3,

        SpeedMin = 40,
        SpeedMax = 140,
        Direction = 90,
        SpreadAngle = 35,
        AngularVelocity = 0,
        InitialRotation = 0,
        RotationRandomness = 1,

        GravityX = 0,
        GravityY = -60,
        Drag = 0.6,
        RadialAcceleration = 0,
        TangentialAcceleration = -30,
        TurbulenceStrength = 80,
        TurbulenceFrequency = 0.1,

        StartSizeMin = 14,
        StartSizeMax = 30,
        EndSize = 6,
        ScaleStart = 1.0,
        ScaleEnd = 0.2,

        StartR = 255, StartG = 170, StartB = 60, StartA = 255,
        MidR = 230, MidG = 60, MidB = 20, MidA = 220,
        EndR = 70, EndG = 70, EndB = 70, EndA = 0,

        FrameCount = 16,
        AnimationSpeed = 1,
        TextureMode = TextureMode.ProceduralSoftGlow,
        TexturePath = null,
        BlendMode = BlendMode.Additive,
    };

    /// <summary>Inward radial pull plus tangential spin: an inward spiral galaxy.</summary>
    public static EmitterSettings CreateCosmicVortex() => new()
    {
        MaxParticles = 2000,
        EmissionRate = 700,
        EmissionShape = EmissionShapeType.Circle,
        ShapeRadius = 120,
        BurstCount = 400,

        LifespanMin = 1.2,
        LifespanMax = 2.2,

        SpeedMin = 40,
        SpeedMax = 120,
        Direction = 90,
        SpreadAngle = 360,
        AngularVelocity = 180,
        InitialRotation = 0,
        RotationRandomness = 1,

        GravityX = 0,
        GravityY = 0,
        Drag = 0.2,
        RadialAcceleration = -200,
        TangentialAcceleration = 200,
        TurbulenceStrength = 0,

        StartSizeMin = 10,
        StartSizeMax = 22,
        EndSize = 4,
        ScaleStart = 1.0,
        ScaleEnd = 0.0,

        StartR = 170, StartG = 90, StartB = 255, StartA = 255,
        MidR = 80, MidG = 150, MidB = 255, MidA = 220,
        EndR = 60, EndG = 230, EndB = 255, EndA = 0,

        FrameCount = 16,
        AnimationSpeed = 1,
        TextureMode = TextureMode.ProceduralSoftGlow,
        TexturePath = null,
        BlendMode = BlendMode.Additive,
    };

    /// <summary>Slow, growing grey plumes driven by turbulence.</summary>
    public static EmitterSettings CreateSmoke() => new()
    {
        MaxParticles = 900,
        EmissionRate = 120,
        EmissionShape = EmissionShapeType.Point,
        BurstCount = 60,

        LifespanMin = 2.5,
        LifespanMax = 4.0,

        SpeedMin = 10,
        SpeedMax = 40,
        Direction = 90,
        SpreadAngle = 25,
        AngularVelocity = 15,
        InitialRotation = 0,
        RotationRandomness = 1,

        GravityX = 0,
        GravityY = -20,
        Drag = 0.8,
        RadialAcceleration = 0,
        TangentialAcceleration = 0,
        TurbulenceStrength = 60,
        TurbulenceFrequency = 0.05,

        StartSizeMin = 20,
        StartSizeMax = 40,
        EndSize = 120,
        ScaleStart = 0.6,
        ScaleEnd = 1.6,

        StartR = 90, StartG = 90, StartB = 95, StartA = 180,
        MidR = 70, MidG = 70, MidB = 75, MidA = 90,
        EndR = 50, EndG = 50, EndB = 55, EndA = 0,

        FrameCount = 16,
        AnimationSpeed = 1,
        TextureMode = TextureMode.ProceduralSoftGlow,
        TexturePath = null,
        BlendMode = BlendMode.AlphaBlend,
    };

    /// <summary>A water jet: fast upward burst pulled back down by gravity.</summary>
    public static EmitterSettings CreateFountain() => new()
    {
        MaxParticles = 2000,
        EmissionRate = 800,
        EmissionShape = EmissionShapeType.Point,
        BurstCount = 400,

        LifespanMin = 1.0,
        LifespanMax = 1.8,

        SpeedMin = 300,
        SpeedMax = 520,
        Direction = 90,
        SpreadAngle = 20,
        AngularVelocity = 0,
        InitialRotation = 0,
        RotationRandomness = 0,

        GravityX = 0,
        GravityY = 700,
        Drag = 0.05,
        RadialAcceleration = 0,
        TangentialAcceleration = 0,
        TurbulenceStrength = 0,
        TurbulenceFrequency = 0.08,

        StartSizeMin = 6,
        StartSizeMax = 12,
        EndSize = 2,
        ScaleStart = 1.0,
        ScaleEnd = 0.4,

        StartR = 120, StartG = 200, StartB = 255, StartA = 255,
        MidR = 60, MidG = 140, MidB = 255, MidA = 220,
        EndR = 200, EndG = 240, EndB = 255, EndA = 0,

        FrameCount = 16,
        AnimationSpeed = 1,
        TextureMode = TextureMode.ProceduralCircle,
        TexturePath = null,
        BlendMode = BlendMode.AlphaBlend,
    };
}
