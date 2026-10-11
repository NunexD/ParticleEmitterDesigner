using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParticleEmitterDesigner.Services;
using ParticleEmitterDesigner.ViewModels.Documents;
using ParticleEmitterDesigner.ViewModels.Tools;

namespace ParticleEmitterDesigner.ViewModels;

/// <summary>
/// Shell view model. Owns the open document tabs and the dockable tool windows,
/// tracks the active document, and mediates workspace-layout persistence and the
/// creation / closing of emitter documents.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly UpdateService _updateService = new();

    [ObservableProperty]
    private EmitterDocumentViewModel? _activeDocument;

    /// <summary>Human-readable running version, shown in the Help menu.</summary>
    [ObservableProperty]
    private string _currentVersionText = $"Version {FormatVersion(UpdateService.CurrentVersion)}";

    /// <summary>Invoked by the shell to persist the current workspace layout.</summary>
    public Action<string>? OnSaveLayout { get; set; }

    /// <summary>Invoked by the shell to restore a workspace layout from disk.</summary>
    public Action<string>? OnRestoreLayout { get; set; }

    /// <summary>Invoked by the shell to rebuild the default workspace layout.</summary>
    public Action? OnResetLayout { get; set; }

    public MainViewModel()
    {
        PresetBrowser = new PresetBrowserViewModel(this);

        Tools = new ObservableCollection<ToolViewModel>
        {
            new ParameterInspectorViewModel(this),
            new TimelineViewModel(this),
            PresetBrowser
        };

        NewDocument();
    }

    /// <summary>Open emitter documents, each shown as a document tab.</summary>
    public ObservableCollection<EmitterDocumentViewModel> Documents { get; } = new();

    /// <summary>Dockable tool windows (inspector, timeline, preset browser).</summary>
    public ObservableCollection<ToolViewModel> Tools { get; }

    /// <summary>Typed reference to the preset/asset browser tool.</summary>
    public PresetBrowserViewModel PresetBrowser { get; }

    /// <summary>Creates a fresh, empty emitter document and activates it.</summary>
    public EmitterDocumentViewModel NewDocument()
    {
        var document = new EmitterDocumentViewModel();
        Documents.Add(document);
        ActiveDocument = document;
        return document;
    }

    /// <summary>Opens the preset/document at <paramref name="path"/> in a new tab.</summary>
    public EmitterDocumentViewModel? OpenDocument(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // Focus an already-open tab rather than opening a duplicate.
        EmitterDocumentViewModel? existing = Documents.FirstOrDefault(
            d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveDocument = existing;
            return existing;
        }

        var document = new EmitterDocumentViewModel(path);
        Documents.Add(document);
        ActiveDocument = document;
        return document;
    }

    /// <summary>Removes <paramref name="document"/> from the workspace.</summary>
    public void CloseDocument(EmitterDocumentViewModel document)
    {
        Documents.Remove(document);

        if (ReferenceEquals(ActiveDocument, document))
        {
            ActiveDocument = Documents.Count > 0 ? Documents[0] : null;
        }
    }

    [RelayCommand]
    private void SaveWorkspaceLayout() => OnSaveLayout?.Invoke(string.Empty);

    [RelayCommand]
    private void RestoreWorkspaceLayout() => OnRestoreLayout?.Invoke(string.Empty);

    [RelayCommand]
    private void ResetWorkspaceLayout() => OnResetLayout?.Invoke();

    /// <summary>Resets the active document's viewport zoom/pan (Ctrl+0).</summary>
    [RelayCommand]
    private void ResetViewport() => ActiveDocument?.RequestResetView?.Invoke();

    /// <summary>Queries GitHub Releases and offers an in-app update.</summary>
    [RelayCommand]
    private Task CheckForUpdates() => _updateService.CheckForUpdatesAsync();

    private static string FormatVersion(Version version) =>
        version.Build >= 0 ? version.ToString(3) : version.ToString(2);
}
