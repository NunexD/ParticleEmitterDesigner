namespace ParticleEmitterDesigner.Models;

/// <summary>
/// How an emitter releases particles over time.
/// </summary>
public enum EmissionMode
{
    /// <summary>Particles are emitted continuously at <see cref="EmitterSettings.EmissionRate"/>.</summary>
    Continuous = 0,

    /// <summary>A single, simultaneous burst of <see cref="EmitterSettings.BurstCount"/>
    /// particles fired only when explicitly triggered (or on reset).</summary>
    OneShotBurst = 1,
}
