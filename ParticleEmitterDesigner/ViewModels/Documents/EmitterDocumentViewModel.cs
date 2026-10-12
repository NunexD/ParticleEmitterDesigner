using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParticleEmitterDesigner.Controls;
using ParticleEmitterDesigner.Engine;
using ParticleEmitterDesigner.Models;
using ParticleEmitterDesigner.Services;

namespace ParticleEmitterDesigner.ViewModels.Documents;

/// <summary>
/// A single, independently simulated emitter document (an open <c>.vfx</c> tab).
/// Each document owns its own parameter set, simulation engine and sprite sheet
/// so several effects can be authored side by side without interference.
/// </summary>
public partial class EmitterDocumentViewModel : ObservableObject
{
    private static int _sequence;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isPlaying = true;

    [ObservableProperty]
    private double _fps;

    [ObservableProperty]
    private int _activeParticles;

    [ObservableProperty]
    private SpriteSheet _sprite;

    [ObservableProperty]
    private string _spriteFileName = "Procedural (4x4)";

    [ObservableProperty]
    private string _presetFileName = "Preset: none";

    public EmitterDocumentViewModel(string? filePath = null)
    {
        ContentId = $"doc:{Interlocked.Increment(ref _sequence)}";
        _sprite = SpriteSheet.CreateProcedural();

        EmitterSettings settings = LoadSettings(filePath);
        Settings = settings;
        Emitter = new ParticleEmitter(Settings, seed: 1000 + _sequence);
        Settings.PropertyChanged += OnSettingsChanged;

        SyncTextureFromSettings();

        _filePath = filePath;
        _title = filePath is null ? NextUntitledName() : Path.GetFileNameWithoutExtension(filePath);
    }

    /// <summary>Stable identity used for workspace layout persistence.</summary>
    public string ContentId { get; }

    /// <summary>
    /// Set by the document view to expose its viewport reset (zoom/pan) to the
    /// shell, so the global Ctrl+0 shortcut can target the active document.
    /// </summary>
    public Action? RequestResetView { get; set; }

    /// <summary>
    /// Set by the view to open a texture file picker. The view owns the dialog;
    /// this keeps the view model free of WPF dialog dependencies.
    /// </summary>
    public Action? RequestSelectTexture { get; set; }

    /// <summary>Shared parameter set bound to the inspector.</summary>
    public EmitterSettings Settings { get; }

    /// <summary>The simulation engine for this document.</summary>
    public ParticleEmitter Emitter { get; }

    /// <summary>Blend modes exposed to the inspector combo box.</summary>
    public Array BlendModes => Enum.GetValues<BlendMode>();

    /// <summary>Emission shapes exposed to the inspector combo box.</summary>
    public Array EmissionShapes => Enum.GetValues<EmissionShapeType>();

    /// <summary>Texture fill modes exposed to the inspector combo box.</summary>
    public Array TextureModes => Enum.GetValues<TextureMode>();

    /// <summary>Advances the simulation and refreshes the HUD counters.</summary>
    public void Tick(double dt)
    {
        if (IsPlaying)
        {
            Emitter.Update(dt);
        }

        ActiveParticles = Emitter.ActiveCount;
    }

    /// <summary>Serialises this document's parameters to <paramref name="path"/>.</summary>
    public bool TrySave(string path)
    {
        try
        {
            PresetSerializer.Save(Settings, path);
            FilePath = path;
            Title = Path.GetFileNameWithoutExtension(path);
            PresetFileName = $"Preset: {Path.GetFileName(path)}";
            IsDirty = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Replaces this document's parameters from a preset file.</summary>
    public bool TryLoad(string path)
    {
        try
        {
            EmitterSettings? loaded = PresetSerializer.Load(path);
            if (loaded is null)
            {
                return false;
            }

            ReplaceSettings(loaded);
            PresetFileName = $"Preset: {Path.GetFileName(path)}";
            IsDirty = true;
            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException)
        {
            return false;
        }
    }

    [RelayCommand]
    private void Play() => IsPlaying = true;

    [RelayCommand]
    private void Pause() => IsPlaying = false;

    [RelayCommand]
    private void TogglePlay() => IsPlaying = !IsPlaying;

    [RelayCommand]
    private void Reset() => Emitter.Reset();

    /// <summary>Fires a one-frame burst of particles.</summary>
    [RelayCommand]
    private void Burst() => Emitter.EmitBurst(Settings.BurstCount);

    /// <summary>Opens the texture file picker (implemented by the view).</summary>
    [RelayCommand]
    private void BrowseTexture() => RequestSelectTexture?.Invoke();

    /// <summary>Reverts to the procedural soft-glow fill.</summary>
    [RelayCommand]
    private void ClearTexture()
    {
        Settings.TexturePath = null;
        Settings.TextureMode = TextureMode.ProceduralSoftGlow;
        SpriteFileName = "Procedural (soft glow)";
    }

    /// <summary>
    /// Applies a built-in preset to this document in place, then reloads the
    /// custom texture (if the preset references one).
    /// </summary>
    public void ApplyPreset(EmitterSettings preset)
    {
        ReplaceSettings(preset);
        PresetFileName = "Preset: built-in";
        IsDirty = true;
    }

    /// <summary>
    /// Loads the image at <paramref name="path"/> as the custom particle texture
    /// and switches the document into <see cref="TextureMode.CustomTexture"/>.
    /// Returns false if the file could not be decoded.
    /// </summary>
    public bool ApplyTexture(string path)
    {
        SpriteSheet? sheet = SpriteSheet.LoadFromFile(path);
        if (sheet is null)
        {
            return false;
        }

        Sprite = sheet;
        SpriteFileName = Path.GetFileName(path);
        Settings.TexturePath = path;
        Settings.TextureMode = TextureMode.CustomTexture;
        return true;
    }

    private void ReplaceSettings(EmitterSettings loaded)
    {
        Settings.PropertyChanged -= OnSettingsChanged;

        // Mutate the observable instance in place so existing bindings survive.
        Settings.CopyFrom(loaded);

        Settings.PropertyChanged += OnSettingsChanged;
        Emitter.SetSettings(Settings);
        Emitter.EnsureCapacity(Settings.MaxParticles);
        Emitter.Reset();

        SyncTextureFromSettings();
    }

    /// <summary>
    /// Reloads the custom-texture sprite when the (possibly just loaded) settings
    /// reference a Decodable PNG, so presets carry their texture with them.
    /// </summary>
    private void SyncTextureFromSettings()
    {
        if (Settings.TextureMode != TextureMode.CustomTexture)
        {
            return;
        }

        string? path = Settings.TexturePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        SpriteSheet? sheet = SpriteSheet.LoadFromFile(path!);
        if (sheet is not null)
        {
            Sprite = sheet;
            SpriteFileName = Path.GetFileName(path!);
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EmitterSettings.MaxParticles))
        {
            Emitter.EnsureCapacity(Settings.MaxParticles);
        }

        IsDirty = true;
    }

    private static EmitterSettings LoadSettings(string? filePath)
    {
        if (filePath is not null)
        {
            EmitterSettings? loaded = PresetSerializer.Load(filePath);
            if (loaded is not null)
            {
                return loaded;
            }
        }

        return new EmitterSettings();
    }

    private static string NextUntitledName() => $"explosion_{_sequence:00}.vfx";

    /// <summary>Fallback caption used by AvalonDock document tab headers.</summary>
    public override string ToString() => Title;
}
