using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ParticleEmitterDesigner.Models;

namespace ParticleEmitterDesigner.Controls;

/// <summary>
/// Interactive canvas for editing a <see cref="CurveData"/>. The curve and its
/// control handles are drawn over a normalised 0..1 grid. Dragging a handle
/// reshapes the curve, double-clicking empty space inserts a point and
/// right-clicking a handle removes it.
/// </summary>
public sealed class CurveSurface : FrameworkElement
{
    private const double Padding = 10.0;
    private const double HandleRadius = 4.5;
    private const double HitRadius = 9.0;

    private static readonly Brush BackgroundBrush = Frozen(Color.FromRgb(0x16, 0x18, 0x21));
    private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0x2C, 0x30, 0x40), 1.0);
    private static readonly Pen AxisPen = FrozenPen(Color.FromRgb(0x3A, 0x40, 0x54), 1.0);
    private static readonly Pen CurvePen = FrozenPen(Color.FromRgb(0x4E, 0xA1, 0xFF), 2.0);
    private static readonly Pen HoverPen = FrozenPen(Colors.White, 1.4);
    private static readonly Brush HandleBrush = Frozen(Color.FromRgb(0x4E, 0xA1, 0xFF));
    private static readonly Pen HandlePen = FrozenPen(Color.FromRgb(0x0D, 0x0F, 0x15), 1.0);

    private CurveData? _curve;
    private int _dragIndex = -1;
    private int _hoverIndex = -1;

    public CurveSurface()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
    }

    /// <summary>The curve being edited. Changing it re-renders the surface.</summary>
    public CurveData? Curve
    {
        get => _curve;
        set
        {
            if (ReferenceEquals(_curve, value))
            {
                return;
            }

            if (_curve is not null)
            {
                _curve.Changed -= OnCurveChanged;
            }

            _curve = value;

            if (_curve is not null)
            {
                _curve.Changed += OnCurveChanged;
            }

            InvalidateVisual();
        }
    }

    private void OnCurveChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));

        double px = Padding;
        double plotW = Math.Max(1.0, w - 2 * Padding);
        double plotH = Math.Max(1.0, h - 2 * Padding);

        for (int i = 0; i <= 4; i++)
        {
            double t = i / 4.0;
            Pen pen = i == 0 || i == 4 ? AxisPen : GridPen;
            double gx = px + t * plotW;
            double gy = px + (1 - t) * plotH;
            dc.DrawLine(pen, new Point(gx, px), new Point(gx, px + plotH));
            dc.DrawLine(pen, new Point(px, gy), new Point(px + plotW, gy));
        }

        CurveData? curve = Curve;
        if (curve is null || curve.Count == 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            const int steps = 160;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                float v = curve.Evaluate((float)t);
                var pt = new Point(px + t * plotW, px + (1 - v) * plotH);
                if (i == 0)
                {
                    ctx.BeginFigure(pt, false, false);
                }
                else
                {
                    ctx.LineTo(pt, true, false);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, CurvePen, geometry);

        for (int i = 0; i < curve.Count; i++)
        {
            CurvePoint p = curve.Points[i];
            var centre = new Point(px + p.X * plotW, px + (1 - p.Y) * plotH);
            dc.DrawEllipse(HandleBrush, HoverPen, centre, HandleRadius, HandleRadius);

            bool active = i == _dragIndex || (i == _hoverIndex && _dragIndex < 0);
            if (active)
            {
                dc.DrawEllipse(null, HoverPen, centre, HandleRadius + 2, HandleRadius + 2);
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Curve is null)
        {
            return;
        }

        Focus();
        Point pos = e.GetPosition(this);
        int hit = HitTestHandle(pos);

        if (hit >= 0)
        {
            _dragIndex = hit;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            (double t, double v) = ToCurve(pos);
            Curve.AddPoint((float)t, (float)v);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Curve is null)
        {
            return;
        }

        Point pos = e.GetPosition(this);

        if (_dragIndex >= 0)
        {
            (double t, double v) = ToCurve(pos);
            Curve.MovePoint(_dragIndex, (float)t, (float)v);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        int hit = HitTestHandle(pos);
        if (hit != _hoverIndex)
        {
            _hoverIndex = hit;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Cross;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (Curve is null)
        {
            return;
        }

        int hit = HitTestHandle(e.GetPosition(this));
        if (hit >= 0)
        {
            Curve.RemovePoint(hit);
            _hoverIndex = -1;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    private (double T, double V) ToCurve(Point pos)
    {
        double plotW = Math.Max(1.0, ActualWidth - 2 * Padding);
        double plotH = Math.Max(1.0, ActualHeight - 2 * Padding);
        double t = (pos.X - Padding) / plotW;
        double v = 1.0 - (pos.Y - Padding) / plotH;
        return (Math.Clamp(t, 0.0, 1.0), Math.Clamp(v, 0.0, 1.0));
    }

    private int HitTestHandle(Point pos)
    {
        CurveData? curve = Curve;
        if (curve is null)
        {
            return -1;
        }

        double plotW = Math.Max(1.0, ActualWidth - 2 * Padding);
        double plotH = Math.Max(1.0, ActualHeight - 2 * Padding);

        int best = -1;
        double bestDist = HitRadius * HitRadius;
        for (int i = 0; i < curve.Count; i++)
        {
            CurvePoint p = curve.Points[i];
            double dx = Padding + p.X * plotW - pos.X;
            double dy = Padding + (1 - p.Y) * plotH - pos.Y;
            double d = dx * dx + dy * dy;
            if (d <= bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return best;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
