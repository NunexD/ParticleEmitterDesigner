using ParticleEmitterDesigner.Models;

namespace ParticleEmitterDesigner.Services;

/// <summary>
/// Top-level structure of a saved <c>.json</c> preset file.
/// </summary>
public sealed class PresetFile
{
    public const int CurrentSchemaVersion = 1;

    public PresetFile() : this(new EmitterSettings())
    {
    }

    public PresetFile(EmitterSettings emitter)
    {
        Emitter = emitter;
    }

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string? Name { get; set; }

    public EmitterSettings Emitter { get; set; }
}