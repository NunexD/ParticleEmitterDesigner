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

        // Size-over-lifetime curve: a two-point linear ramp from ScaleStart to
        // ScaleEnd, emitted as a CurveTexture for scale_curve.
        sb.AppendLine("[sub_resource type=\"Curve\" id=\"1_scale\"]");
        sb.AppendLine($"min_value = {F(Math.Min(settings.ScaleStart, settings.ScaleEnd))}");
        sb.AppendLine($"max_value = {F(Math.Max(settings.ScaleStart, settings.ScaleEnd))}");
        sb.AppendLine(
            $"_data = [Vector2(0, {F(settings.ScaleStart)}), 0.0, 0.0, 0, 0, " +
            $"Vector2(1, {F(settings.ScaleEnd)}), 0.0, 0.0, 0, 0]");
        sb.AppendLine("point_count = 2");
        sb.AppendLine();
        sb.AppendLine("[sub_resource type=\"CurveTexture\" id=\"1_scaletex\"]");
        sb.AppendLine("curve = SubResource(\"1_scale\")");
        sb.AppendLine();

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
        sb.AppendLine($"angular_velocity_min = {F(settings.AngularVelocity)}");
        sb.AppendLine($"angular_velocity_max = {F(settings.AngularVelocity)}");
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
}
