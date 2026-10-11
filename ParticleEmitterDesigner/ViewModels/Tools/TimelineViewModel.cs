using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParticleEmitterDesigner.ViewModels.Documents;

namespace ParticleEmitterDesigner.ViewModels.Tools;

/// <summary>
/// Bottom-dock timeline / keyframe sequencer scaffold. It exposes playback
/// controls for the active document plus a simple normalised keyframe track that
/// scrub-time markers can be added to and removed from.
/// </summary>
public sealed partial class TimelineViewModel : ToolViewModel
{
    [ObservableProperty]
    private double _durationSeconds = 1.0;

    [ObservableProperty]
    private double _playhead;

    [ObservableProperty]
    private double? _selectedKeyframe;

    public TimelineViewModel(MainViewModel shell)
        : base("Timeline", "tool.timeline", DockSide.Bottom)
    {
        Shell = shell;
    }

    /// <summary>Back-reference to the shell so playback binds the active document.</summary>
    public MainViewModel Shell { get; }

    public EmitterDocumentViewModel? ActiveDocument => Shell.ActiveDocument;

    /// <summary>Keyframe positions in seconds along the timeline track.</summary>
    public ObservableCollection<double> Keyframes { get; } = new();

    /// <summary>Normalised playhead position in the range [0, 1] for the ruler.</summary>
    public double PlayheadNormalised =>
        DurationSeconds <= 0 ? 0 : Math.Clamp(Playhead / DurationSeconds, 0, 1);

    [RelayCommand]
    private void AddKeyframe()
    {
        double t = Math.Clamp(Playhead, 0, DurationSeconds);
        if (!Keyframes.Contains(t))
        {
            Keyframes.Add(t);
            SortKeyframes();
        }
    }

    [RelayCommand]
    private void RemoveKeyframe()
    {
        if (SelectedKeyframe is { } key)
        {
            Keyframes.Remove(key);
        }
    }

    partial void OnPlayheadChanged(double value) => OnPropertyChanged(nameof(PlayheadNormalised));

    partial void OnDurationSecondsChanged(double value) => OnPropertyChanged(nameof(PlayheadNormalised));

    private void SortKeyframes()
    {
        var ordered = Keyframes.OrderBy(x => x).ToList();
        Keyframes.Clear();
        foreach (double key in ordered)
        {
            Keyframes.Add(key);
        }
    }
}
