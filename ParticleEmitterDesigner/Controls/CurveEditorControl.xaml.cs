using System.Windows;
using System.Windows.Controls;
using ParticleEmitterDesigner.Models;

namespace ParticleEmitterDesigner.Controls;

/// <summary>
/// Editor for a single <see cref="CurveData"/>: hosts a <see cref="CurveSurface"/>
/// plus a row of preset buttons. The curve is exposed as a two-way bindable
/// dependency property so it can be bound directly to
/// <see cref="EmitterSettings.ScaleCurve"/>.
/// </summary>
public partial class CurveEditorControl : UserControl
{
    public static readonly DependencyProperty CurveProperty = DependencyProperty.Register(
        nameof(Curve), typeof(CurveData), typeof(CurveEditorControl),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnCurvePropertyChanged));

    public CurveEditorControl()
    {
        InitializeComponent();
        Loaded += (_, _) => PushToSurface();
    }

    public CurveData? Curve
    {
        get => (CurveData?)GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    private static void OnCurvePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CurveEditorControl)d).PushToSurface();

    private void PushToSurface()
    {
        if (Surface is not null)
        {
            Surface.Curve = Curve;
        }
    }

    private void ApplyPreset(CurveData preset)
    {
        if (Curve is null)
        {
            Curve = preset;
            return;
        }

        Curve.CopyFrom(preset);
        PushToSurface();
    }

    private void OnPresetLinear(object sender, RoutedEventArgs e) => ApplyPreset(CurveData.Linear());

    private void OnPresetEaseIn(object sender, RoutedEventArgs e) => ApplyPreset(CurveData.EaseIn());

    private void OnPresetEaseOut(object sender, RoutedEventArgs e) => ApplyPreset(CurveData.EaseOut());

    private void OnPresetBell(object sender, RoutedEventArgs e) => ApplyPreset(CurveData.Bell());

    private void OnPresetPopFade(object sender, RoutedEventArgs e) => ApplyPreset(CurveData.FastPopSlowFade());

    private void OnPresetFlat(object sender, RoutedEventArgs e) => ApplyPreset(CurveData.Flat());
}
