using ParticleEmitterDesigner.Models;

namespace ParticleEmitterDesigner.Engine;

/// <summary>
/// Allocation-free particle simulation engine.
///
/// Particles live in a single, reused <see cref="Particle"/>[] buffer ("object
/// pool"). Live particles are packed at the front of the array in the range
/// [0, <see cref="ActiveCount"/>). Dead particles are removed with a cheap
/// swap-with-last operation, so no collections or per-frame allocations occur.
/// </summary>
public sealed class ParticleEmitter
{
    private EmitterSettings _settings;
    private readonly Random _random;

    private Particle[] _pool;
    private int _activeCount;
    private double _emissionAccumulator;
    private double _elapsed;

    public ParticleEmitter(EmitterSettings settings, int seed = 12345)
    {
        _settings = settings;
        _random = new Random(seed);
        _pool = new Particle[Math.Max(1, settings.MaxParticles)];
    }

    /// <summary>The shared parameter set driving the simulation.</summary>
    public EmitterSettings Settings => _settings;

    /// <summary>Swaps the backing parameter set (used when a preset is loaded).</summary>
    public void SetSettings(EmitterSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Backing particle buffer. Only indices [0, ActiveCount) are valid.</summary>
    public Particle[] Particles => _pool;

    /// <summary>Number of currently live particles.</summary>
    public int ActiveCount => _activeCount;

    /// <summary>Total simulated time in seconds (drives the turbulence field).</summary>
    public double Elapsed => _elapsed;

    /// <summary>Emitter origin in device-independent pixels (set by the view).</summary>
    public float OriginX { get; set; }
    public float OriginY { get; set; }

    /// <summary>Ensures the pool can hold at least <paramref name="capacity"/> particles.</summary>
    public void EnsureCapacity(int capacity)
    {
        if (capacity <= _pool.Length) return;

        var newPool = new Particle[capacity];
        Array.Copy(_pool, newPool, _activeCount);
        _pool = newPool;
    }

    /// <summary>Clears every live particle and resets emission timing.</summary>
    public void Reset()
    {
        _activeCount = 0;
        _emissionAccumulator = 0;
        _elapsed = 0;

        // One-shot emitters have no continuous stream, so give the user
        // something to look at by firing a burst immediately on reset.
        if (_settings.EmissionMode == EmissionMode.OneShotBurst)
        {
            EmitBurst(_settings.BurstCount);
        }
    }

    /// <summary>
    /// Fires a single <see cref="EmitterSettings.BurstCount"/> burst. This is the
    /// "Trigger / Replay" action used by one-shot emitters.
    /// </summary>
    public void TriggerBurst() => EmitBurst(_settings.BurstCount);

    /// <summary>
    /// Immediately spawns <paramref name="count"/> particles (clamped to the
    /// remaining pool capacity) in a single frame.
    /// </summary>
    public void EmitBurst(int count)
    {
        int max = Math.Min(_settings.MaxParticles, _pool.Length);
        for (int i = 0; i < count && _activeCount < max; i++)
        {
            Spawn(ref _pool[_activeCount]);
            _activeCount++;
        }
    }

    /// <summary>Advances the simulation by <paramref name="dt"/> seconds.</summary>
    public void Update(double dt)
    {
        if (dt <= 0) return;
        if (dt > 0.25) dt = 0.25; // guard against huge stalls / debugger pauses

        _elapsed += dt;

        Emit(dt);
        Integrate((float)dt);
    }

    private void Emit(double dt)
    {
        // One-shot emitters never stream; they only fire when TriggerBurst is
        // called (or on Reset).
        if (_settings.EmissionMode == EmissionMode.OneShotBurst)
        {
            return;
        }

        int max = Math.Min(_settings.MaxParticles, _pool.Length);
        _emissionAccumulator += _settings.EmissionRate * dt;

        int toEmit = (int)_emissionAccumulator;
        if (toEmit <= 0) return;
        _emissionAccumulator -= toEmit;

        for (int i = 0; i < toEmit && _activeCount < max; i++)
        {
            Spawn(ref _pool[_activeCount]);
            _activeCount++;
        }
    }

    private void Spawn(ref Particle p)
    {
        double dir = _settings.Direction * Math.PI / 180.0;
        double halfSpread = _settings.SpreadAngle * 0.5;
        double radius = Math.Max(0.0, _settings.ShapeRadius);
        double boxX = Math.Max(0.0, _settings.ShapeBoxExtentsX);
        double boxY = Math.Max(0.0, _settings.ShapeBoxExtentsY);

        double offX = 0;
        double offY = 0;
        double primaryAngle = dir; // measured in maths convention (90 = up)
        double velX;
        double velY;
        double speed = Lerp(_settings.SpeedMin, _settings.SpeedMax, _random.NextDouble());

        switch (_settings.EmissionShape)
        {
            case EmissionShapeType.Circle:
            {
                // Uniform sampling of a filled disk: r = R * sqrt(rand).
                double phi = _random.NextDouble() * Math.Tau;
                double rr = radius * Math.Sqrt(_random.NextDouble());
                offX = Math.Cos(phi) * rr;
                offY = Math.Sin(phi) * rr;
                // Velocity points radially outward through the spawn point.
                double jitter = Lerp(-halfSpread, halfSpread, _random.NextDouble()) * Math.PI / 180.0;
                velX = Math.Cos(phi + jitter) * speed;
                velY = Math.Sin(phi + jitter) * speed;
                goto Assign;
            }
            case EmissionShapeType.Sphere:
            {
                // Strictly on the ring border: r = R.
                double phi = _random.NextDouble() * Math.Tau;
                offX = Math.Cos(phi) * radius;
                offY = Math.Sin(phi) * radius;
                double jitter = Lerp(-halfSpread, halfSpread, _random.NextDouble()) * Math.PI / 180.0;
                velX = Math.Cos(phi + jitter) * speed;
                velY = Math.Sin(phi + jitter) * speed;
                goto Assign;
            }
            case EmissionShapeType.Box:
            {
                offX = (_random.NextDouble() - 0.5) * boxX;
                offY = (_random.NextDouble() - 0.5) * boxY;
                break;
            }
            case EmissionShapeType.Cone:
            {
                // Cone apex at the origin, axis along the configured direction.
                double dist = _random.NextDouble() * radius;
                double coneHalf = halfSpread * Math.PI / 180.0;
                double crossRadius = dist * Math.Tan(coneHalf);

                // Axis (maths convention) and its perpendicular.
                double ax = Math.Cos(dir);
                double ay = Math.Sin(dir);
                double jitterLine = (_random.NextDouble() * 2.0 - 1.0) * crossRadius;

                double ux = ax * dist + (-ay) * jitterLine;
                double uy = ay * dist + ax * jitterLine;

                offX = ux;
                offY = -uy; // maths-UP -> screen-down
                break;
            }
        }

        // Point / Box / Cone: direction +/- spread, maths convention.
        double spread = Lerp(-halfSpread, halfSpread, _random.NextDouble()) * Math.PI / 180.0;
        double angle = primaryAngle + spread;
        velX = Math.Cos(angle) * speed;
        velY = -Math.Sin(angle) * speed;

    Assign:
        p.X = (float)(OriginX + offX);
        p.Y = (float)(OriginY + offY);

        // Speed-over-lifetime: when engaged, the spawn velocity is stored as the
        // base (scaled by the curve each frame) and the live velocity starts at
        // zero pending forces. Otherwise the spawn velocity is the live velocity
        // (the legacy, curve-free path).
        if (_settings.UseSpeedCurve && !_settings.SpeedCurve.IsFlat())
        {
            p.BaseVelocityX = (float)velX;
            p.BaseVelocityY = (float)velY;
            p.VelocityX = 0f;
            p.VelocityY = 0f;
        }
        else
        {
            p.BaseVelocityX = 0f;
            p.BaseVelocityY = 0f;
            p.VelocityX = (float)velX;
            p.VelocityY = (float)velY;
        }

        p.Age = 0f;
        p.Lifespan = (float)Math.Max(0.0001, Lerp(_settings.LifespanMin, _settings.LifespanMax, _random.NextDouble()));

        p.StartSize = (float)Lerp(_settings.StartSizeMin, _settings.StartSizeMax, _random.NextDouble());
        p.EndSize = (float)_settings.EndSize;

        p.ScaleStart = (float)_settings.ScaleStart;
        p.ScaleEnd = (float)_settings.ScaleEnd;

        // Seed the dynamic scale so a particle drawn before its first Integrate
        // still has a sensible size.
        p.CurrentScale = _settings.UseScaleCurve
            ? _settings.ScaleCurve.Evaluate(0f) * p.ScaleStart
            : p.ScaleStart;

        p.StartR = ToByte(_settings.StartR);
        p.StartG = ToByte(_settings.StartG);
        p.StartB = ToByte(_settings.StartB);
        p.StartA = ToByte(_settings.StartA);

        p.MidR = ToByte(_settings.MidR);
        p.MidG = ToByte(_settings.MidG);
        p.MidB = ToByte(_settings.MidB);
        p.MidA = ToByte(_settings.MidA);

        p.EndR = ToByte(_settings.EndR);
        p.EndG = ToByte(_settings.EndG);
        p.EndB = ToByte(_settings.EndB);
        p.EndA = ToByte(_settings.EndA);

        // Flipbook start frame: either the first cell, or a random offset when
        // the emitter is configured to desync particles.
        int totalFrames = Math.Clamp(_settings.AnimColumns, 1, 16) * Math.Clamp(_settings.AnimRows, 1, 16);
        p.FrameOffset = _settings.RandomInitialFrame
            ? (float)(_random.NextDouble() * totalFrames)
            : 0f;
        p.CurrentFrame = 0;

        // Initial sprite rotation (degrees -> radians) plus an optional scatter
        // of up to +/- 180 degrees at full randomness. Angular speed is stored
        // per particle so the renderer only reads the pooled struct.
        double rotationJitter = (_random.NextDouble() * 2.0 - 1.0) * _settings.RotationRandomness * Math.PI;
        p.Rotation = (float)(_settings.InitialRotation * Math.PI / 180.0 + rotationJitter);
        p.AngularVelocity = (float)(_settings.AngularVelocity * Math.PI / 180.0);
    }

    private void Integrate(float dt)
    {
        float gx = (float)_settings.GravityX;
        float gy = (float)_settings.GravityY;
        float drag = (float)_settings.Drag;
        float dragFactor = 1f - drag * dt;
        if (dragFactor < 0f) dragFactor = 0f;

        float radAccel = (float)_settings.RadialAcceleration;
        float tanAccel = (float)_settings.TangentialAcceleration;
        bool hasAccel = radAccel != 0f || tanAccel != 0f;
        float originX = OriginX;
        float originY = OriginY;

        float turbStrength = (float)_settings.TurbulenceStrength;
        bool turbulence = turbStrength > 0f;
        float turbFreq = (float)_settings.TurbulenceFrequency;
        double time = _elapsed;

        // Flipbook playback: the active frame is derived from the particle's
        // normalised age so it advances in lock-step with its lifetime.
        bool animated = _settings.IsAnimated;
        int totalFrames = Math.Clamp(_settings.AnimColumns, 1, 16) * Math.Clamp(_settings.AnimRows, 1, 16);
        float animSpeed = Math.Clamp((float)_settings.AnimSpeed, 0f, 1000f);

        // Scale-over-lifetime: the curve (when enabled) modulates the
        // ScaleStart -> ScaleEnd base ramp.
        bool useScaleCurve = _settings.UseScaleCurve;
        CurveData? scaleCurve = useScaleCurve ? _settings.ScaleCurve : null;

        // Speed-over-lifetime: the curve scales the spawn velocity; forces act on
        // the live velocity. Disabled or flat curves use a multiplier of 1.
        CurveData? speedCurve = _settings.UseSpeedCurve && !_settings.SpeedCurve.IsFlat()
            ? _settings.SpeedCurve
            : null;

        int count = _activeCount;

        for (int i = 0; i < count; i++)
        {
            ref Particle p = ref _pool[i];

            p.Age += dt;
            if (p.Age >= p.Lifespan)
            {
                // Swap-with-last removal keeps the array densely packed.
                count--;
                _pool[i] = _pool[count];
                i--; // re-process the swapped-in particle
                continue;
            }

            float life = p.Lifespan > 0f ? p.Age / p.Lifespan : 1f;

            p.VelocityX += gx * dt;
            p.VelocityY += gy * dt;

            if (hasAccel)
            {
                // Direction from the emitter origin to the particle.
                float rx = p.X - originX;
                float ry = p.Y - originY;
                float len = MathF.Sqrt(rx * rx + ry * ry);
                if (len > 1e-4f)
                {
                    float inv = 1f / len;
                    rx *= inv;
                    ry *= inv;

                    // Tangential is the radial vector rotated 90 degrees.
                    float tx = -ry;
                    float ty = rx;

                    p.VelocityX += (rx * radAccel + tx * tanAccel) * dt;
                    p.VelocityY += (ry * radAccel + ty * tanAccel) * dt;
                }
            }

            if (turbulence)
            {
                // Perturb velocity from a scrolling Perlin field sampled at the
                // particle's position; two offset samples give independent X/Y.
                double nx = p.X * turbFreq + time * 0.5;
                double ny = p.Y * turbFreq - time * 0.5;
                float noiseX = (float)Noise2D.Sample(nx, ny);
                float noiseY = (float)Noise2D.Sample(nx + 73.1, ny + 41.7);

                p.VelocityX += noiseX * turbStrength * dt;
                p.VelocityY += noiseY * turbStrength * dt;
            }

            // Exponential dampening (friction): v *= (1 - drag * dt).
            p.VelocityX *= dragFactor;
            p.VelocityY *= dragFactor;

            float speedScale = speedCurve is not null ? speedCurve.Evaluate(life) : 1f;

            p.X += (p.BaseVelocityX * speedScale + p.VelocityX) * dt;
            p.Y += (p.BaseVelocityY * speedScale + p.VelocityY) * dt;

            p.Rotation += p.AngularVelocity * dt;

            float baseScale = p.ScaleStart + (p.ScaleEnd - p.ScaleStart) * life;
            p.CurrentScale = scaleCurve is not null ? scaleCurve.Evaluate(life) * baseScale : baseScale;

            if (animated)
            {
                int frame = (int)MathF.Floor(life * totalFrames * animSpeed + p.FrameOffset) % totalFrames;
                if (frame < 0) frame += totalFrames;
                p.CurrentFrame = frame;
            }
        }

        _activeCount = count;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static byte ToByte(double value) =>
        (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
