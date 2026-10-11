using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ParticleEmitterDesigner.Controls;

/// <summary>
/// Compact colour editor: an RGB swatch, an editable <c>#RRGGBB</c> hex field and
/// an alpha slider. The channel values are exposed as four two-way bindable
/// dependency properties so the control can be pointed straight at the emitter
/// settings colour stops.
/// </summary>
public partial class ColorField : UserControl
{
    private bool _updating;

    public static readonly DependencyProperty RProperty = DependencyProperty.Register(
        nameof(R), typeof(double), typeof(ColorField),
        new FrameworkPropertyMetadata(255.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChannelChanged));

    public static readonly DependencyProperty GProperty = DependencyProperty.Register(
        nameof(G), typeof(double), typeof(ColorField),
        new FrameworkPropertyMetadata(255.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChannelChanged));

    public static readonly DependencyProperty BProperty = DependencyProperty.Register(
        nameof(B), typeof(double), typeof(ColorField),
        new FrameworkPropertyMetadata(255.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChannelChanged));

    public static readonly DependencyProperty AProperty = DependencyProperty.Register(
        nameof(A), typeof(double), typeof(ColorField),
        new FrameworkPropertyMetadata(255.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChannelChanged));

    public ColorField()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public double R
    {
        get => (double)GetValue(RProperty);
        set => SetValue(RProperty, value);
    }

    public double G
    {
        get => (double)GetValue(GProperty);
        set => SetValue(GProperty, value);
    }

    public double B
    {
        get => (double)GetValue(BProperty);
        set => SetValue(BProperty, value);
    }

    public double A
    {
        get => (double)GetValue(AProperty);
        set => SetValue(AProperty, value);
    }

    private static void OnChannelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColorField)d).Refresh();

    private static byte ToByte(double value) =>
        (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

    private void Refresh()
    {
        if (_updating || Swatch is null)
        {
            return;
        }

        _updating = true;
        try
        {
            byte r = ToByte(R);
            byte g = ToByte(G);
            byte b = ToByte(B);
            byte a = ToByte(A);

            Swatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));

            if (!HexBox.IsKeyboardFocusWithin)
            {
                HexBox.Text = $"#{r:X2}{g:X2}{b:X2}";
            }

            AlphaSlider.Value = a;
            AlphaLabel.Text = a.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnAlphaChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating)
        {
            return;
        }

        A = e.NewValue;
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e) => CommitHex();

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
    }

    private void CommitHex()
    {
        if (_updating || !TryParseHex(HexBox.Text, out byte r, out byte g, out byte b))
        {
            Refresh();
            return;
        }

        R = r;
        G = g;
        B = b;
    }

    private static bool TryParseHex(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string hex = text.Trim().TrimStart('#');

        // Support the short #RGB form as well as the full #RRGGBB form.
        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        if (hex.Length != 6 ||
            !byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
            !byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
            !byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
        {
            return false;
        }

        return true;
    }
}
