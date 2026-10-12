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
    public EmitterSettings()
    {
        _scaleCurve.Changed += OnScaleCurveChanged;
        _speedCurve.Changed += OnSpeedCurveChanged;
    }

    // ----- Emission ---------------------------------------------------------

    [ObservableProperty]
    private int _maxParticles = 2000;

    [ObservableProperty]
    private double _emissionRate = 600;          // particles per second

    [ObservableProperty]
    private EmissionMode _emissionMode = EmissionMode.Continuous;

    [ObservableProperty]
    private EmissionShapeType _emissionShape = EmissionShapeType.Point;

    [ObservableProperty]
    private double _shapeRadius = 50;            // radius of Circle / Sphere / Cone

    [ObservableProperty]
    private double _shapeBoxExtentsX = 100;      // full width of the Box shape

    [ObservableProperty]
    private double _shapeBoxExtentsY = 50;       // full height of the Box shape

    [ObservableProperty]
    private int _burstCount = 50;                // particles fired by the Burst command

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

    [ObservableProperty]
    private double _initialRotation = 0;         // degrees; sprite rotation at spawn (0-360)

    [ObservableProperty]
    private double _rotationRandomness = 0;      // 0-1; scatters the initial rotation over +/- 180 deg

    // Speed-over-lifetime: when enabled, SpeedCurve (a normalised multiplier)
    // scales the particle's spawn velocity as it ages. Disabled by default so
    // existing behaviour (constant speed plus forces) is unchanged.
    [ObservableProperty]
    private bool _useSpeedCurve = false;

    private CurveData _speedCurve = CurveData.Flat();

    /// <summary>
    /// Speed-over-lifetime multiplier curve. When <see cref="UseSpeedCurve"/> is
    /// true the emitter scales each particle's spawn velocity by this curve as it
    /// ages (forces such as gravity are then added on top). A flat 1.0 curve
    /// leaves the spawn velocity untouched.
    /// </summary>
    public CurveData SpeedCurve
    {
        get => _speedCurve;
        set
        {
            CurveData incoming = value ?? CurveData.Flat();
            if (ReferenceEquals(_speedCurve, incoming))
            {
                return;
            }

            _speedCurve.Changed -= OnSpeedCurveChanged;
            _speedCurve = incoming.Clone();
            _speedCurve.Changed += OnSpeedCurveChanged;
            OnPropertyChanged(nameof(SpeedCurve));
        }
    }

    private void OnSpeedCurveChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(SpeedCurve));

    // ----- Forces -----------------------------------------------------------

    [ObservableProperty]
    private double _gravityX = 0;                // pixels / second^2

    [ObservableProperty]
    private double _gravityY = 220;              // pixels / second^2 (down)

    [ObservableProperty]
    private double _drag = 0;                    // exponential velocity damping per second

    [ObservableProperty]
    private double _radialAcceleration = 0;      // pixels / second^2 along origin -> particle

    [ObservableProperty]
    private double _tangentialAcceleration = 0;  // pixels / second^2 perpendicular to radial

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

    // Multiplier applied to the particle size over its lifetime (0 = invisible).
    [ObservableProperty]
    private double _scaleStart = 1.0;

    [ObservableProperty]
    private double _scaleEnd = 0.0;

    // When enabled, the particle's scale-over-lifetime is shaped by ScaleCurve
    // (a normalised multiplier) on top of the ScaleStart -> ScaleEnd ramp.
    [ObservableProperty]
    private bool _useScaleCurve = true;

    private CurveData _scaleCurve = CurveData.Flat();

    /// <summary>
    /// Scale-over-lifetime multiplier curve. When <see cref="UseScaleCurve"/> is
    /// true the emitter samples this per particle and multiplies it by the
    /// <see cref="ScaleStart"/>/<see cref="ScaleEnd"/> base ramp. The default is
    /// a flat 1.0 curve, i.e. no change to the base ramp.
    /// </summary>
    public CurveData ScaleCurve
    {
        get => _scaleCurve;
        set
        {
            CurveData incoming = value ?? CurveData.Flat();
            if (ReferenceEquals(_scaleCurve, incoming))
            {
                return;
            }

            _scaleCurve.Changed -= OnScaleCurveChanged;
            _scaleCurve = incoming.Clone();
            _scaleCurve.Changed += OnScaleCurveChanged;
            OnPropertyChanged(nameof(ScaleCurve));
        }
    }

    private void OnScaleCurveChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(ScaleCurve));

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

    // ----- Sprite-sheet / flipbook animation -------------------------------

    [ObservableProperty]
    private bool _isAnimated;                    // play a custom texture as a flipbook

    [ObservableProperty]
    private int _animColumns = 1;                // sheet columns (1 .. 16)

    [ObservableProperty]
    private int _animRows = 1;                   // sheet rows (1 .. 16)

    [ObservableProperty]
    private double _animSpeed = 1.0;             // animation cycles over a particle's lifetime

    [ObservableProperty]
    private bool _randomInitialFrame;            // desync particles with a random start frame

    [ObservableProperty]
    private TextureMode _textureMode = TextureMode.ProceduralSoftGlow;

    [ObservableProperty]
    private string? _texturePath;               // source PNG for CustomTexture (nullable)

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
