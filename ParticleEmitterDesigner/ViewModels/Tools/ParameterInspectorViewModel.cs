using ParticleEmitterDesigner.ViewModels.Documents;

namespace ParticleEmitterDesigner.ViewModels.Tools;

/// <summary>
/// Left-hand parameter inspector. It does not own any state of its own; instead
/// it surfaces the currently active document's parameter set to the bound view
/// (matched through <see cref="Shell"/>).
/// </summary>
public sealed class ParameterInspectorViewModel : ToolViewModel
{
    public ParameterInspectorViewModel(MainViewModel shell)
        : base("Inspector", "tool.inspector", DockSide.Left)
    {
        Shell = shell;
    }

    /// <summary>Back-reference to the shell so the view can bind the active document.</summary>
    public MainViewModel Shell { get; }

    /// <summary>Convenience accessor for the active document (may be null).</summary>
    public EmitterDocumentViewModel? ActiveDocument => Shell.ActiveDocument;
}
