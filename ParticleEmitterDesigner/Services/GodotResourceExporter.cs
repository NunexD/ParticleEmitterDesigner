using System.Globalization;
using System.IO;
using System.Text;
using ParticleEmitterDesigner.Models;

namespace ParticleEmitterDesigner.Services;

/// <summary>
/// Tunables that control how the emitter parameter set is translated into Godot
/// world units. Defaults target Godot 4's 2D convention where 1 world unit maps
/// to one screen pixel.
/// </summary>
public sealed class GodotExportOptions
{
    /// <summary>Emitters define sizes/shapes in pixels; this converts them to
    /// Godot world units (2D default: 1 pixel per unit).</summary>
    public double PixelsPerUnit { get; init; } = 1.0;

    /// <summary>Nominal particle texture size (pixels) used to express the
    /// absolute start/end sizes as Godot <c>scale_min</c>/<c>scale_max</c></summary>
    public double ReferenceTextureSize { get; init; } = 64.0;

    /// <summary>Resource path written for the optional albedo texture, e.g.
    /// <c>res://vfx/flipbook.png</c>. Left empty to omit texture references.</summary>
    public string TexturePath { get; init; } = string.Empty;
}

/// <summary>
/// Serialises an <see cref="EmitterSettings"/> instance into the text format of a
/// Godot 4 <c>ParticleProcessMaterial</c> resource (<c>.tres</c>).
///
/// The mapping follows the emitter's maths convention (0 deg = right, 90 deg =
/// up) and converts the direction into a Godot Y-down <c>Vector3</c>. Values are
/// emitted with the invariant culture so files import indentically regardless of
/// the authoring machine's locale.
/// </summary>
public static class GodotResourceExporter
{
    public static string Build(EmitterSettings settings, GodotExportOptions? options = null)
    {
        options ??= new GodotExportOptions();

        double ppu = options.PixelsPerUnit <= 0 ? 1.0 : options.PixelsPerUnit;
        double refTex = options.ReferenceTextureSize <= 0 ? 64.0 : options.ReferenceTextureSize;

        // Direction: maths convention (0 = right, CCW positive) -> Godot Y-down.
        double theta = settings.Direction * Math.PI / 180.0;
        double dirX = Math.Cos(theta);
        double dirY = -Math.Sin(theta);

        // Emission shape translation.
        int shape = MapShape(settings.EmissionShape);
        double radius = settings.ShapeRadius / ppu;
        double boxX = settings.ShapeBoxExtentsX * 0.5 / ppu;
        double boxY = settings.ShapeBoxExtentsY * 0.5 / ppu;

        double scaleMin = settings.StartSizeMin / refTex;
        double scaleMax = Math.Max(scaleMin, settings.StartSizeMax / refTex);

        var sb = new StringBuilder();
        sb.AppendLine("[gd_resource type=\"ParticleProcessMaterial\" format=3]");
        sb.AppendLine();

        bool hasTexture = !string.IsNullOrWhiteSpace(options.TexturePath);
        if (hasTexture)
        {
            // External resources must be declared before the [resource] block.
            sb.AppendLine($"[ext_resource type=\"Texture2D\" path=\"{options.TexturePath}\" id=\"1_tex\"]");
            sb.AppendLine();
        }

        // Three-stop colour ramp exported as a Gradient + GradientTexture1D pair.
        sb.AppendLine("[sub_resource type=\"Gradient\" id=\"1_grad\"]");
        sb.AppendLine("offsets = PackedFloat32Array(0, 0.5, 1)");
        sb.AppendLine(
            $"colors = PackedColorArray({RampColor(settings.StartR, settings.StartG, settings.StartB, settings.StartA)}, " +
            $"{RampColor(settings.MidR, settings.MidG, settings.MidB, settings.MidA)}, " +
            $"{RampColor(settings.EndR, settings.EndG, settings.EndB, settings.EndA)})");
        sb.AppendLine();
        sb.AppendLine("[sub_resource type=\"GradientTexture1D\" id=\"1_gradtex\"]");
        sb.AppendLine("gradient = SubResource(\"1_grad\")");
        sb.AppendLine();

        // Size-over-lifetime curve, emitted as a CurveTexture for scale_curve.
        AppendScaleCurve(sb, settings);

        // Speed-over-lifetime curve: only emitted when the editor curve is
        // actually in use (Godot's default is a constant speed), as a second
        // Curve / CurveTexture pair referenced by velocity_curve.
        bool emitVelocityCurve = settings.UseSpeedCurve && !settings.SpeedCurve.IsFlat();
        if (emitVelocityCurve)
        {
            AppendCurveTexture(sb, "2_vel", "2_veltex", settings.SpeedCurve);
            sb.AppendLine();
        }

        sb.AppendLine("[resource]");
        sb.AppendLine($"emission_shape = {shape}");
        if (shape is 1 or 2)
        {
            sb.AppendLine($"emission_sphere_radius = {F(radius)}");
        }
        else if (shape == 3)
        {
            sb.AppendLine($"emission_box_extents = {Vec(boxX, boxY, 0)}");
        }

        sb.AppendLine($"direction = {Vec(dirX, dirY, 0)}");
        sb.AppendLine($"spread = {F(settings.SpreadAngle)}");
        sb.AppendLine($"initial_velocity_min = {F(settings.SpeedMin)}");
        sb.AppendLine($"initial_velocity_max = {F(Math.Max(settings.SpeedMin, settings.SpeedMax))}");
        double angleJitter = Math.Max(0.0, Math.Min(1.0, settings.RotationRandomness)) * 180.0;
        sb.AppendLine($"angle_min = {F(settings.InitialRotation - angleJitter)}");
        sb.AppendLine($"angle_max = {F(settings.InitialRotation + angleJitter)}");
        sb.AppendLine($"angular_velocity_min = {F(settings.AngularVelocity)}");
        sb.AppendLine($"angular_velocity_max = {F(settings.AngularVelocity)}");

        // Flipbook playback maps directly onto Godot's material animation: a
        // speed of 1.0 runs the full sheet once over the particle's lifetime,
        // matching the designer's AnimSpeed semantics. Random start positions
        // are expressed by spreading the animation offset over 0..1.
        if (settings.IsAnimated)
        {
            sb.AppendLine($"anim_speed_min = {F(Math.Clamp(settings.AnimSpeed, 0.0, 1000.0))}");
            sb.AppendLine($"anim_speed_max = {F(Math.Clamp(settings.AnimSpeed, 0.0, 1000.0))}");
            if (settings.RandomInitialFrame)
            {
                sb.AppendLine("anim_offset_min = 0.0");
                sb.AppendLine("anim_offset_max = 1.0");
            }
        }

        sb.AppendLine($"gravity = {Vec(settings.GravityX, settings.GravityY, 0)}");
        sb.AppendLine($"radial_accel_min = {F(settings.RadialAcceleration)}");
        sb.AppendLine($"radial_accel_max = {F(settings.RadialAcceleration)}");
        sb.AppendLine($"tangential_accel_min = {F(settings.TangentialAcceleration)}");
        sb.AppendLine($"tangential_accel_max = {F(settings.TangentialAcceleration)}");
        sb.AppendLine($"damping_min = {F(settings.Drag)}");
        sb.AppendLine($"damping_max = {F(settings.Drag)}");
        sb.AppendLine($"scale_min = {F(scaleMin)}");
        sb.AppendLine($"scale_max = {F(scaleMax)}");
        sb.AppendLine("scale_curve = SubResource(\"1_scaletex\")");
        if (emitVelocityCurve)
        {
            sb.AppendLine("velocity_curve = SubResource(\"2_veltex\")");
        }
        // The ramp carries the authored colours, so the base modulation stays
        // white to avoid tinting the gradient twice.
        sb.AppendLine("color = Color(1, 1, 1, 1)");
        sb.AppendLine("color_ramp = SubResource(\"1_gradtex\")");

        if (hasTexture)
        {
            sb.AppendLine("albedo_texture = ExtResource(\"1_tex\")");
        }

        return sb.ToString();
    }

    public static void Export(EmitterSettings settings, string path, GodotExportOptions? options = null)
    {
        File.WriteAllText(path, Build(settings, options), new UTF8Encoding(false));
    }

    /// <summary>
    /// Copies the emitter's custom texture into <paramref name="targetDirectory"/>
    /// so the exported <c>.tres</c> can reference it with a self-contained
    /// <c>res://</c> path. Returns the copied file name, or <c>null</c> when the
    /// emitter does not use a custom texture (or it can no longer be found).
    /// </summary>
    public static string? PackageTexture(EmitterSettings settings, string targetDirectory)
    {
        if (settings.TextureMode != TextureMode.CustomTexture)
        {
            return null;
        }

        string? source = settings.TexturePath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            return null;
        }

        Directory.CreateDirectory(targetDirectory);
        string fileName = Path.GetFileName(source!);
        string destination = Path.Combine(targetDirectory, fileName);

        if (!string.Equals(Path.GetFullPath(source!), Path.GetFullPath(destination),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source!, destination, overwrite: true);
        }

        return fileName;
    }

    private static int MapShape(EmissionShapeType shape)
    {
        // Godot ParticleProcessMaterial.EmissionShape:
        // 0 = point, 1 = sphere (filled), 2 = sphere surface (ring), 3 = box.
        return shape switch
        {
            EmissionShapeType.Point => 0,
            EmissionShapeType.Circle => 1,
            EmissionShapeType.Sphere => 2,
            EmissionShapeType.Box => 3,
            // A cone is approximated by a point emitter whose direction + spread
            // already encode the cone's opening angle.
            EmissionShapeType.Cone => 0,
            _ => 0
        };
    }

    private static string F(double value) =>
        value.ToString("0.#####", CultureInfo.InvariantCulture);

    private static string Vec(double x, double y, double z) =>
        $"Vector3({F(x)}, {F(y)}, {F(z)})";

    private static string RampColor(double r, double g, double b, double a) =>
        $"{F(r / 255.0)}, {F(g / 255.0)}, {F(b / 255.0)}, {F(a / 255.0)}";

    /// <summary>
    /// Writes the <c>Curve</c> + <c>CurveTexture</c> sub-resources used for
    /// <c>scale_curve</c>.
    ///
    /// When <see cref="EmitterSettings.UseScaleCurve"/> is enabled the curve is
    /// baked from the current <see cref="EmitterSettings.ScaleCurve"/> with the
    /// ScaleStart -> ScaleEnd base ramp folded in, so the exported multiplier
    /// matches the designer's preview. A flat identity curve (the default) is
    /// treated the same as the disabled mode, producing a two-point ramp.
    ///
        // Godot stores each point as: position, left_tangent, right_tangent,
        // left_mode, right_mode, where the tangents are slopes (dy/dx).
    /// </summary>
    private static void AppendScaleCurve(StringBuilder sb, EmitterSettings settings)
    {
        double start = settings.ScaleStart;
        double end = settings.ScaleEnd;

        CurveData? curve = settings.UseScaleCurve ? settings.ScaleCurve : null;
        bool useCurve = curve is not null && !curve.IsFlat();

        sb.AppendLine("[sub_resource type=\"Curve\" id=\"1_scale\"]");

        if (!useCurve)
        {
            // Two-point ramp from ScaleStart to ScaleEnd. The tangents are the
            // segment slope (end - start) so the interpolation is truly linear.
            double slope = end - start;
            sb.AppendLine($"min_value = {F(Math.Min(start, end))}");
            sb.AppendLine($"max_value = {F(Math.Max(start, end))}");
            sb.AppendLine(
                $"_data = [Vector2(0, {F(start)}), 0.0, {F(slope)}, 0, 0, " +
                $"Vector2(1, {F(end)}), {F(slope)}, 0.0, 0, 0]");
            sb.AppendLine("point_count = 2");
        }
        else
        {
            // Bake the curve with the ScaleStart -> ScaleEnd base ramp folded in
            // (the engine multiplies the two), matching the designer preview.
            static double Ramp(double s, double e, double x) => s + (e - s) * x;
            CurveData source = curve!;
            double Value(double x) => source.Evaluate((float)x) * Ramp(start, end, x);

            const int intervals = 32;
            var xs = new double[intervals + 1];
            var ys = new double[intervals + 1];
            double minValue = double.MaxValue;
            double maxValue = double.MinValue;
            for (int i = 0; i <= intervals; i++)
            {
                double x = i / (double)intervals;
                double y = Value(x);
                xs[i] = x;
                ys[i] = y;
                minValue = Math.Min(minValue, y);
                maxValue = Math.Max(maxValue, y);
            }

            if (maxValue - minValue < 1e-4)
            {
                minValue -= 0.5;
                maxValue += 0.5;
            }

            sb.AppendLine($"min_value = {F(minValue)}");
            sb.AppendLine($"max_value = {F(maxValue)}");

            var data = new StringBuilder();
            for (int i = 0; i <= intervals; i++)
            {
                double slope = Derivative(Value, xs[i]);
                if (i > 0)
                {
                    data.Append(", ");
                }

                data.Append($"Vector2({F(xs[i])}, {F(ys[i])}), {F(slope)}, {F(slope)}, 0, 0");
            }

            sb.AppendLine($"_data = [{data}]");
            sb.AppendLine($"point_count = {intervals + 1}");
        }

        sb.AppendLine();
        sb.AppendLine("[sub_resource type=\"CurveTexture\" id=\"1_scaletex\"]");
        sb.AppendLine("curve = SubResource(\"1_scale\")");
        sb.AppendLine();
    }

    private static double Derivative(Func<double, double> f, double x)
    {
        const double h = 1e-3;
        double lo = Math.Max(0.0, x - h);
        double hi = Math.Min(1.0, x + h);
        return hi - lo < 1e-9 ? 0.0 : (f(hi) - f(lo)) / (hi - lo);
    }

    /// <summary>
    /// Writes a <c>Curve</c> + <c>CurveTexture</c> sub-resource pair that mirrors
    /// <paramref name="curve"/> one-to-one, storing each point's authored tangents
    /// (slopes) so the exported velocity_curve matches the editor preview.
    /// </summary>
    private static void AppendCurveTexture(StringBuilder sb, string curveId, string textureId, CurveData curve)
    {
        var points = curve.Points;

        double minValue = double.MaxValue;
        double maxValue = double.MinValue;
        foreach (CurvePoint point in points)
        {
            minValue = Math.Min(minValue, point.Y);
            maxValue = Math.Max(maxValue, point.Y);
        }

        if (points.Count == 0)
        {
            minValue = 0.0;
            maxValue = 1.0;
        }
        else if (maxValue - minValue < 1e-4)
        {
            minValue -= 0.5;
            maxValue += 0.5;
        }

        sb.AppendLine($"[sub_resource type=\"Curve\" id=\"{curveId}\"]");
        sb.AppendLine($"min_value = {F(minValue)}");
        sb.AppendLine($"max_value = {F(maxValue)}");

        var data = new StringBuilder();
        for (int i = 0; i < points.Count; i++)
        {
            CurvePoint point = points[i];
            if (i > 0)
            {
                data.Append(", ");
            }

            data.Append(
                $"Vector2({F(point.X)}, {F(point.Y)}), {F(point.LeftTangent)}, {F(point.RightTangent)}, 0, 0");
        }

        sb.AppendLine($"_data = [{data}]");
        sb.AppendLine($"point_count = {points.Count}");
        sb.AppendLine();
        sb.AppendLine($"[sub_resource type=\"CurveTexture\" id=\"{textureId}\"]");
        sb.AppendLine($"curve = SubResource(\"{curveId}\")");
    }
}
