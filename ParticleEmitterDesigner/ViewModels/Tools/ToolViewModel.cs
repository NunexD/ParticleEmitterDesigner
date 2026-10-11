using CommunityToolkit.Mvvm.ComponentModel;

namespace ParticleEmitterDesigner.ViewModels.Tools;

/// <summary>Preferred docking edge for a tool window.</summary>
public enum DockSide
{
    Left,
    Right,
    Bottom
}

/// <summary>
/// Base class for every dockable tool window. <see cref="ContentId"/> is the
/// stable identity used by AvalonDock's layout serializer to reconnect a saved
/// layout with the live view model, so it must remain constant across runs.
/// </summary>
public abstract partial class ToolViewModel : ObservableObject
{
    protected ToolViewModel(string title, string contentId, DockSide preferredLocation)
    {
        Title = title;
        ContentId = contentId;
        PreferredLocation = preferredLocation;
    }

    /// <summary>Caption shown on the tool window's tab / header.</summary>
    public string Title { get; }

    /// <summary>Stable identity used for workspace layout persistence.</summary>
    public string ContentId { get; }

    /// <summary>Which dock the tool prefers on first show / layout reset.</summary>
    public DockSide PreferredLocation { get; }

    /// <summary>Fallback caption used by AvalonDock headers when no template is set.</summary>
    public override string ToString() => Title;
}
