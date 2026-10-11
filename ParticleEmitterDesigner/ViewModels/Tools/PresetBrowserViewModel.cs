using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ParticleEmitterDesigner.ViewModels.Tools;

/// <summary>A single entry in the preset browser / asset library list.</summary>
public sealed class PresetItemViewModel
{
    public PresetItemViewModel(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name { get; }

    public string Path { get; }

    public override string ToString() => Name;
}

/// <summary>
/// Right-dock browser: a list of reusable JSON presets plus a catalogue of Godot
/// assets (exported <c>.tres</c> materials and flipbook <c>.png</c> textures).
/// </summary>
public sealed partial class PresetBrowserViewModel : ToolViewModel
{
    [ObservableProperty]
    private PresetItemViewModel? _selectedPreset;

    [ObservableProperty]
    private string _presetFolder;

    [ObservableProperty]
    private string _assetFolder;

    public PresetBrowserViewModel(MainViewModel shell)
        : base("Presets & Assets", "tool.presets", DockSide.Right)
    {
        Shell = shell;

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _presetFolder = Path.Combine(documents, "ParticleEmitterDesigner", "Presets");
        _assetFolder = Path.Combine(documents, "ParticleEmitterDesigner", "GodotAssets");

        Refresh();
    }

    public MainViewModel Shell { get; }

    /// <summary>Available preset files discovered on disk.</summary>
    public ObservableCollection<PresetItemViewModel> Presets { get; } = new();

    /// <summary>Exported Godot assets (.tres / .png) discovered on disk.</summary>
    public ObservableCollection<PresetItemViewModel> Assets { get; } = new();

    [RelayCommand]
    private void Refresh()
    {
        Presets.Clear();
        foreach (string file in EnumerateFiles(PresetFolder, "*.json"))
        {
            Presets.Add(new PresetItemViewModel(Path.GetFileNameWithoutExtension(file), file));
        }

        Assets.Clear();
        foreach (string file in EnumerateFiles(AssetFolder, "*.tres"))
        {
            Assets.Add(new PresetItemViewModel(Path.GetFileName(file), file));
        }

        foreach (string file in EnumerateFiles(AssetFolder, "*.png"))
        {
            Assets.Add(new PresetItemViewModel(Path.GetFileName(file), file));
        }
    }

    /// <summary>Loads the selected preset into the active document.</summary>
    [RelayCommand]
    private void ApplyPreset()
    {
        if (SelectedPreset is { } preset && Shell.ActiveDocument is { } document)
        {
            document.TryLoad(preset.Path);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string folder, string pattern)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(folder, pattern))
        {
            yield return file;
        }
    }
}
