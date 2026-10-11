using CommunityToolkit.Mvvm.ComponentModel;

namespace ParticleEmitterDesigner.Models;

/// <summary>
/// Live, observable set of emitter parameters. The view model exposes a single
/// instance of this type for the UI to bind against, while the
/// <see cref="Engine.ParticleEmitter"/> reads the same instance every frame.
/// This keeps a single source of truth and avoids duplicating parameter state.
/// </summary>
public partial class EmitterSettings : ObservableObject
{
    // ----- Emission ---------------------------------------------------------

    [ObservableProperty]
    private int _maxParticles = 2000;

    [ObservableProperty]
    private double _emissionRate = 600;          // particles per second

    [ObservableProperty]
    private EmitterShape _shape = EmitterShape.Point;

    [ObservableProperty]
    private double _emitterSize = 60;            // diameter / extent of the shape

    [ObservableProperty]
    private int _burstCount = 200;               // particles fired by the Burst command

    // ----- Emitter transform (world space, device-independent pixels) -------

    [ObservableProperty]
    private double _emitterX = 0;                // horizontal position of the emitter origin

    [ObservableProperty]
    private double _emitterY = 0;                // vertical position of the emitter origin

    // ----- Lifetime ---------------------------------------------------------

    [ObservableProperty]
    private double _lifespanMin = 0.8;           // seconds

    [ObservableProperty]
    private double _lifespanMax = 1.6;           // seconds

    // ----- Velocity ---------------------------------------------------------

    [ObservableProperty]
    private double _speedMin = 80;               // pixels / second

    [ObservableProperty]
    private double _speedMax = 220;              // pixels / second

    [ObservableProperty]
    private double _direction = 90;              // degrees, 0 = right, 90 = up

    [ObservableProperty]
    private double _spreadAngle = 40;            // degrees; 360 = full radial

    [ObservableProperty]
    private double _angularVelocity = 0;         // degrees / second of rotation

    // ----- Forces -----------------------------------------------------------

    [ObservableProperty]
    private double _gravityX = 0;                // pixels / second^2

    [ObservableProperty]
    private double _gravityY = 220;              // pixels / second^2 (down)

    [ObservableProperty]
    private double _drag = 0;                    // exponential velocity damping per second

    [ObservableProperty]
    private double _turbulenceStrength = 0;      // noise-driven velocity perturbation

    [ObservableProperty]
    private double _turbulenceFrequency = 0.08;  // spatial frequency of the noise field

    // ----- Size (linear interpolation over lifetime) ------------------------

    [ObservableProperty]
    private double _startSizeMin = 12;           // diameter in pixels

    [ObservableProperty]
    private double _startSizeMax = 26;           // diameter in pixels

    [ObservableProperty]
    private double _endSize = 2;

    // ----- Start colour (RGBA, 0-255) --------------------------------------

    [ObservableProperty]
    private double _startR = 255;

    [ObservableProperty]
    private double _startG = 180;

    [ObservableProperty]
    private double _startB = 80;

    [ObservableProperty]
    private double _startA = 255;

    // ----- Mid colour (RGBA, 0-255; applied at ~50% of the particle's age) --

    [ObservableProperty]
    private double _midR = 255;

    [ObservableProperty]
    private double _midG = 220;

    [ObservableProperty]
    private double _midB = 120;

    [ObservableProperty]
    private double _midA = 255;

    // ----- Sprite-sheet animation ------------------------------------------

    [ObservableProperty]
    private double _frameCount = 16;             // frames used from the sheet (1 .. 64)

    [ObservableProperty]
    private double _animationSpeed = 1;          // full animation cycles over a particle's lifetime

    // ----- End colour (RGBA, 0-255) ----------------------------------------

    [ObservableProperty]
    private double _endR = 255;

    [ObservableProperty]
    private double _endG = 40;

    [ObservableProperty]
    private double _endB = 0;

    [ObservableProperty]
    private double _endA = 0;

    // ----- Rendering --------------------------------------------------------

    [ObservableProperty]
    private BlendMode _blendMode = BlendMode.Additive;

    /// <summary>
    /// Copies every observable parameter from <paramref name="other"/> into this
    /// instance in place. Replacing values (rather than the object) keeps all
    /// existing data bindings alive when a preset is loaded.
    /// </summary>
    public void CopyFrom(EmitterSettings other)
    {
        if (ReferenceEquals(this, other))
        {
            return;
        }

        foreach (var property in typeof(EmitterSettings).GetProperties())
        {
            if (property.CanRead && property.CanWrite)
            {
                property.SetValue(this, property.GetValue(other));
            }
        }
    }
}
