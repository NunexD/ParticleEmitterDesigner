using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParticleEmitterDesigner.Models;

namespace ParticleEmitterDesigner.Services;

/// <summary>
/// Serializes the full emitter parameter set to / from a human-readable JSON
/// preset file. The preset format is versioned so parameter sets can evolve
/// without breaking previously saved files.
/// </summary>
public static class PresetSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Writes <paramref name="settings"/> to <paramref name="path"/>.</summary>
    public static void Save(EmitterSettings settings, string path)
    {
        var preset = new PresetFile(settings);
        string json = JsonSerializer.Serialize(preset, Options);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Reads an emitter preset from <paramref name="path"/>. Returns null if the
    /// file is missing, unreadable or not a valid preset.
    /// </summary>
    public static EmitterSettings? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            PresetFile? preset = JsonSerializer.Deserialize<PresetFile>(json, Options);
            if (preset?.Emitter is null)
            {
                return null;
            }

            EnsurePresetCompatibility(preset);
            return preset.Emitter;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void EnsurePresetCompatibility(PresetFile preset)
    {
        if (preset.SchemaVersion > PresetFile.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Preset schema version {preset.SchemaVersion} is newer than this build supports.");
        }
    }
}