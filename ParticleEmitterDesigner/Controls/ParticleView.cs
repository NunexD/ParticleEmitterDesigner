using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ParticleEmitterDesigner.Engine;
using ParticleEmitterDesigner.Models;

namespace ParticleEmitterDesigner.Controls;

/// <summary>
/// High-performance particle viewport.
///
/// No WPF FrameworkElements are created per particle. Instead every particle is
/// rasterised by <see cref="ParticleRasterizer"/> directly into a reused
/// <see cref="WriteableBitmap"/> back buffer on top of a dark background, and an
/// interactive editor gizmo is drawn over the top with vector primitives.
/// Blending (alpha / additive), sprite-sheet flipbook animation, rotation and
/// three-stop colour interpolation over life are all handled by the rasteriser.
///
/// Interaction:
/// <list type="bullet">
/// <item>Drag the origin handle to reposition the emitter in world space.</item>
/// <item>Drag the direction handle to rotate the emission angle.</item>
/// <item>Middle-drag pans the camera; the mouse wheel zooms about the cursor.</item>
/// <item><c>Ctrl+0</c> (routed via the shell) resets zoom and re-centres the emitter.</item>
/// </list>
/// </summary>
public sealed class ParticleView : FrameworkElement
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 5.0;

    // Gizmo metrics, in device-independent pixels (constant on-screen size).
    private const double SpreadArcRadius = 46.0;
    private const double DirectionHandleDistance = 64.0;
    private const double OriginHandleRadius = 8.0;
    private const double DirectionHandleRadius = 8.0;

    private static readonly Brush AccentBrush = MakeBrush(0x4E, 0xA1, 0xFF);
    private static readonly Brush HandleFill = MakeBrush(0xE7, 0xE9, 0xF2);
    private static readonly Brush SpreadFill = MakeBrush(0x4E, 0xA1, 0xFF, 0x33);
    private static readonly Pen AccentPen = MakePen(AccentBrush, 1.5);
    private static readonly Pen SpreadPen = MakePen(MakeBrush(0x4E, 0xA1, 0xFF, 0xAA), 1.2);
    private static readonly Pen ShapeBoundsPen = MakeDashedPen(MakeBrush(0x4E, 0xA1, 0xFF, 0xCC), 1.2);
    private static readonly Pen HandlePen = MakePen(MakeBrush(0x0D, 0x0F, 0x15), 1.5);

    private WriteableBitmap? _bitmap;
    private int _pixelWidth;
    private int _pixelHeight;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    private double _zoom = 1.0;
    private Point _pan;
    private bool _cameraInitialized;

    private bool _panning;
    private bool _draggingOrigin;
    private bool _draggingDirection;
    private Point _lastMouse;

    private SpriteSheet? _sheet;
    private Color _background = Color.FromRgb(0x0A, 0x0B, 0x0F);
    private uint _backgroundPacked;

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(ParticleView), new PropertyMetadata(1.0));

    public static readonly DependencyProperty ZoomPercentProperty = DependencyProperty.Register(
        nameof(ZoomPercent), typeof(double), typeof(ParticleView), new PropertyMetadata(100.0));

    public ParticleView()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        _sheet = SpriteSheet.CreateProcedural();
        _backgroundPacked = PackBgra(_background);
    }

    /// <summary>Current camera zoom factor (1.0 == 100%).</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        private set => SetValue(ZoomProperty, value);
    }

    /// <summary>Zoom expressed as a percentage, bound by the viewport HUD.</summary>
    public double ZoomPercent
    {
        get => (double)GetValue(ZoomPercentProperty);
        private set => SetValue(ZoomPercentProperty, value);
    }

    /// <summary>The simulation engine rendered by this viewport.</summary>
    public ParticleEmitter? Emitter { get; set; }

    /// <summary>
    /// The sprite sheet currently used for rendering. Owned by the active
    /// document but consumed here; setting it re-uses the existing atlas
    /// directly in the render loop.
    /// </summary>
    public SpriteSheet? Sprite
    {
        get => _sheet;
        set => _sheet = value;
    }

    /// <summary>Clear colour of the viewport.</summary>
    public Color BackgroundColor
    {
        get => _background;
        set
        {
            _background = value;
            _backgroundPacked = PackBgra(value);
            InvalidateVisual();
        }
    }

    /// <summary>Resets zoom to 100% and re-centres the view on the emitter.</summary>
    public void ResetView()
    {
        _zoom = 1.0;
        UpdateZoomProperties();
        _cameraInitialized = false;
        CenterCamera();
        InvalidateVisual();
    }

    /// <summary>
    /// Loads a sprite sheet from <paramref name="path"/> and returns true on
    /// success. Texture bytes are copied once; the render loop only touches a
    /// plain byte buffer afterwards.
    /// </summary>
    public bool LoadSpriteSheet(string path)
    {
        SpriteSheet? sheet = SpriteSheet.LoadFromFile(path);
        if (sheet is null)
        {
            return false;
        }

        _sheet = sheet;
        return true;
    }

    /// <summary>Reverts to the built-in procedural, animated sheet.</summary>
    public void SetDefaultSprite()
    {
        _sheet = SpriteSheet.CreateProcedural();
    }

    /// <summary>
    /// Rasterises the current emitter state and schedules a redraw. Called once
    /// per rendering frame (see the document view's CompositionTarget loop).
    /// </summary>
    public void Render()
    {
        if (Emitter is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        _dpiScaleX = dpi.DpiScaleX;
        _dpiScaleY = dpi.DpiScaleY;

        int pw = Math.Max(1, (int)Math.Round(ActualWidth * _dpiScaleX));
        int ph = Math.Max(1, (int)Math.Round(ActualHeight * _dpiScaleY));

        if (_bitmap is null || pw != _pixelWidth || ph != _pixelHeight)
        {
            _pixelWidth = pw;
            _pixelHeight = ph;
            _bitmap = new WriteableBitmap(pw, ph, 96.0 * _dpiScaleX, 96.0 * _dpiScaleY,
                PixelFormats.Bgra32, null);
        }

        if (!_cameraInitialized)
        {
            CenterCamera();
        }

        // Emitter origin comes from the settings so the gizmo can move it.
        EmitterSettings settings = Emitter.Settings;
        Emitter.OriginX = (float)settings.EmitterX;
        Emitter.OriginY = (float)settings.EmitterY;

        Rasterize();

        InvalidateVisual();
    }

    private unsafe void Rasterize()
    {
        int pw = _pixelWidth;
        int ph = _pixelHeight;

        double scaleX = _zoom * _dpiScaleX;
        double scaleY = _zoom * _dpiScaleY;
        double offsetX = _pan.X * _dpiScaleX;
        double offsetY = _pan.Y * _dpiScaleY;

        _bitmap!.Lock();
        byte* back = (byte*)_bitmap.BackBuffer;
        int stride = _bitmap.BackBufferStride;

        // Clear the whole buffer to the background colour (packed 32-bit writes).
        uint bg = _backgroundPacked;
        uint* clear = (uint*)back;
        int total = pw * ph;
        for (int i = 0; i < total; i++)
        {
            clear[i] = bg;
        }

        ParticleRasterizer.Rasterize(
            back, pw, ph, stride,
            Emitter!, scaleX, scaleY, offsetX, offsetY, _sheet,
            0, 0, pw - 1, ph - 1);

        _bitmap.AddDirtyRect(new Int32Rect(0, 0, pw, ph));
        _bitmap.Unlock();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (_bitmap is not null)
        {
            dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        DrawGizmo(dc);
    }

    // ===================== Editor gizmo =====================

    private void DrawGizmo(DrawingContext dc)
    {
        if (Emitter is null)
        {
            return;
        }

        EmitterSettings settings = Emitter.Settings;
        Point origin = WorldToScreen(settings.EmitterX, settings.EmitterY);
        double theta = settings.Direction * Math.PI / 180.0;
        double half = settings.SpreadAngle * 0.5 * Math.PI / 180.0;

        // Wireframe bounds matching the selected emission shape.
        DrawShapeBounds(dc, settings, origin);

        // Spread arc (symmetric around the emission direction).
        StreamGeometry arc = BuildArc(origin, SpreadArcRadius, theta - half, theta + half, 40);
        dc.DrawGeometry(null, SpreadPen, arc);

        // Spread boundary lines.
        Point left = PointOnCircle(origin, SpreadArcRadius, theta + half);
        Point right = PointOnCircle(origin, SpreadArcRadius, theta - half);
        dc.DrawLine(SpreadPen, origin, left);
        dc.DrawLine(SpreadPen, origin, right);

        // Direction pointer.
        Point directionHandle = PointOnCircle(origin, DirectionHandleDistance, theta);
        dc.DrawLine(AccentPen, origin, directionHandle);
        dc.DrawEllipse(AccentBrush, HandlePen, directionHandle, DirectionHandleRadius, DirectionHandleRadius);

        // Origin handle on top.
        dc.DrawEllipse(HandleFill, HandlePen, origin, OriginHandleRadius, OriginHandleRadius);
        dc.DrawEllipse(AccentBrush, null, origin, OriginHandleRadius * 0.4, OriginHandleRadius * 0.4);
    }

    /// <summary>
    /// Draws a dashed wireframe matching the active emission shape, centred on
    /// the emitter origin: a circle for Circle/Sphere, a rectangle for Box and a
    /// pair of boundary rays for Cone. Dimensions are world-space, so they scale
    /// with the camera zoom.
    /// </summary>
    private void DrawShapeBounds(DrawingContext dc, EmitterSettings settings, Point origin)
    {
        double radius = settings.ShapeRadius * _zoom;

        switch (settings.EmissionShape)
        {
            case EmissionShapeType.Circle:
            case EmissionShapeType.Sphere:
                if (radius > 0.5)
                {
                    dc.DrawEllipse(null, ShapeBoundsPen, origin, radius, radius);
                }

                break;

            case EmissionShapeType.Box:
            {
                double w = settings.ShapeBoxExtentsX * _zoom;
                double h = settings.ShapeBoxExtentsY * _zoom;
                if (w > 0.5 || h > 0.5)
                {
                    var rect = new Rect(
                        origin.X - w * 0.5, origin.Y - h * 0.5,
                        w < 0 ? 0 : w, h < 0 ? 0 : h);
                    dc.DrawRectangle(null, ShapeBoundsPen, rect);
                }

                break;
            }

            case EmissionShapeType.Cone:
                if (radius > 0.5)
                {
                    double theta = settings.Direction * Math.PI / 180.0;
                    double half = settings.SpreadAngle * 0.5 * Math.PI / 180.0;
                    dc.DrawLine(ShapeBoundsPen, origin, PointOnCircle(origin, radius, theta + half));
                    dc.DrawLine(ShapeBoundsPen, origin, PointOnCircle(origin, radius, theta - half));
                }

                break;
        }
    }

    private static StreamGeometry BuildArc(Point center, double radius, double from, double to, int steps)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(PointOnCircle(center, radius, from), false, false);
            for (int i = 1; i <= steps; i++)
            {
                double a = from + (to - from) * i / steps;
                ctx.LineTo(PointOnCircle(center, radius, a), true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Point at <paramref name="radius"/> from <paramref name="center"/>
    /// along the maths convention (0 = right, CCW positive, screen Y down).</summary>
    private static Point PointOnCircle(Point center, double radius, double angle) => new(
        center.X + Math.Cos(angle) * radius,
        center.Y - Math.Sin(angle) * radius);

    // ===================== Interaction =====================

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            _lastMouse = e.GetPosition(this);
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();

        if (Emitter is null)
        {
            base.OnMouseLeftButtonDown(e);
            return;
        }

        Point cursor = e.GetPosition(this);

        if (HitDirectionHandle(cursor))
        {
            _draggingDirection = true;
        }
        else if (HitOriginHandle(cursor))
        {
            _draggingOrigin = true;
        }

        if (_draggingOrigin || _draggingDirection)
        {
            CaptureMouse();
            e.Handled = true;
        }

        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Mouse.Captured != this)
        {
            base.OnMouseMove(e);
            return;
        }

        Point position = e.GetPosition(this);

        if (_panning)
        {
            _pan += position - _lastMouse;
            _lastMouse = position;
            InvalidateVisual();
        }
        else if (_draggingOrigin && Emitter is not null)
        {
            Point world = ScreenToWorld(position);
            Emitter.Settings.EmitterX = world.X;
            Emitter.Settings.EmitterY = world.Y;
            InvalidateVisual();
        }
        else if (_draggingDirection && Emitter is not null)
        {
            Point center = WorldToScreen(Emitter.Settings.EmitterX, Emitter.Settings.EmitterY);
            Vector delta = position - center;
            double angle = Math.Atan2(-delta.Y, delta.X) * 180.0 / Math.PI;
            if (angle < 0)
            {
                angle += 360.0;
            }

            Emitter.Settings.Direction = angle;
            InvalidateVisual();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_draggingOrigin || _draggingDirection)
        {
            _draggingOrigin = false;
            _draggingDirection = false;
            if (Mouse.Captured == this)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_panning && e.ChangedButton == MouseButton.Middle)
        {
            _panning = false;
            Cursor = Cursors.Cross;
            if (Mouse.Captured == this)
            {
                ReleaseMouseCapture();
            }
            e.Handled = true;
        }

        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Point cursor = e.GetPosition(this);

        // Keep the world point under the cursor fixed while zooming.
        Point world = ScreenToWorld(cursor);
        double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _pan = new Point(cursor.X - world.X * _zoom, cursor.Y - world.Y * _zoom);

        UpdateZoomProperties();
        InvalidateVisual();
        e.Handled = true;
        base.OnMouseWheel(e);
    }

    private bool HitOriginHandle(Point cursor)
    {
        if (Emitter is null)
        {
            return false;
        }

        Point origin = WorldToScreen(Emitter.Settings.EmitterX, Emitter.Settings.EmitterY);
        return (cursor - origin).Length <= OriginHandleRadius + 4.0;
    }

    private bool HitDirectionHandle(Point cursor)
    {
        if (Emitter is null)
        {
            return false;
        }

        EmitterSettings settings = Emitter.Settings;
        Point origin = WorldToScreen(settings.EmitterX, settings.EmitterY);
        double theta = settings.Direction * Math.PI / 180.0;
        Point handle = PointOnCircle(origin, DirectionHandleDistance, theta);
        return (cursor - handle).Length <= DirectionHandleRadius + 4.0;
    }

    // ===================== Camera helpers =====================

    private void CenterCamera()
    {
        if (Emitter is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            _cameraInitialized = false;
            return;
        }

        EmitterSettings settings = Emitter.Settings;
        _pan = new Point(
            ActualWidth * 0.5 - settings.EmitterX * _zoom,
            ActualHeight * 0.5 - settings.EmitterY * _zoom);
        _cameraInitialized = true;
    }

    private void UpdateZoomProperties()
    {
        Zoom = _zoom;
        ZoomPercent = _zoom * 100.0;
    }

    private Point ScreenToWorld(Point screen) => new(
        (screen.X - _pan.X) / _zoom,
        (screen.Y - _pan.Y) / _zoom);

    private Point WorldToScreen(double worldX, double worldY) => new(
        worldX * _zoom + _pan.X,
        worldY * _zoom + _pan.Y);

    // ===================== Helpers =====================

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b, byte a)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static Pen MakeDashedPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0.0)
        };
        pen.Freeze();
        return pen;
    }

    private static uint PackBgra(Color c) =>
        (uint)((c.A << 24) | (c.R << 16) | (c.G << 8) | c.B);
}
