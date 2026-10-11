using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ParticleEmitterDesigner.Controls;

/// <summary>
/// Builds a horizontal three-stop <see cref="LinearGradientBrush"/> from the
/// twelve channel values (Start/Mid/End RGBA, in that order) supplied by a
/// multi-binding, so the inspector can preview the colour ramp.
/// </summary>
public sealed class ColorRampBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 12)
        {
            return Brushes.Transparent;
        }

        // Abandon the preview while the bindings are unresolved (no document).
        foreach (object value in values)
        {
            if (value is not double and not float and not int)
            {
                return Brushes.Transparent;
            }
        }

        var brush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(ToColor(values, 0), 0.0),
                new(ToColor(values, 4), 0.5),
                new(ToColor(values, 8), 1.0)
            },
            new Point(0, 0),
            new Point(1, 0));

        brush.Freeze();
        return brush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();

    private static Color ToColor(object[] values, int index) => Color.FromArgb(
        ToByte(values[index + 3]), ToByte(values[index]), ToByte(values[index + 1]), ToByte(values[index + 2]));

    private static byte ToByte(object value)
    {
        double d = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return (byte)(d < 0 ? 0 : d > 255 ? 255 : d);
    }
}
