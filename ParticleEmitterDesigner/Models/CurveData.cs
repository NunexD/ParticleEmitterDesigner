using System.Text.Json.Serialization;

namespace ParticleEmitterDesigner.Models;

/// <summary>
/// A single control point on a <see cref="CurveData"/>. <see cref="X"/> is the
/// normalised time (0..1) and <see cref="Y"/> the curve value. The tangents are
/// slopes (<c>dy/dx</c>) entering (<see cref="LeftTangent"/>) and leaving
/// (<see cref="RightTangent"/>) the point, used for cubic Hermite interpolation.
/// </summary>
public struct CurvePoint
{
    public CurvePoint(float x, float y, float leftTangent = 0f, float rightTangent = 0f)
    {
        X = x;
        Y = y;
        LeftTangent = leftTangent;
        RightTangent = rightTangent;
    }

    public float X { get; set; }

    public float Y { get; set; }

    public float LeftTangent { get; set; }

    public float RightTangent { get; set; }
}

/// <summary>
/// A lightweight, editable piecewise cubic-Hermite curve. Points are kept sorted
/// by <see cref="CurvePoint.X"/> and always include a point at x = 0 and x = 1 so
/// the curve fully spans the normalised lifetime range.
/// </summary>
public sealed class CurveData
{
    private readonly List<CurvePoint> _points = new();

    /// <summary>Creates a flat curve at y = 1 (an identity multiplier).</summary>
    public CurveData()
    {
        _points.Add(new CurvePoint(0f, 1f));
        _points.Add(new CurvePoint(1f, 1f));
    }

    public CurveData(IEnumerable<CurvePoint> points)
    {
        _points.AddRange(points);
        Normalize();
    }

    /// <summary>Raised whenever a point is added, moved, removed or replaced.</summary>
    public event EventHandler? Changed;

    /// <summary>Control points, kept sorted by <see cref="CurvePoint.X"/>.</summary>
    public List<CurvePoint> Points
    {
        get => _points;
        set
        {
            _points.Clear();
            if (value is not null)
            {
                _points.AddRange(value);
            }

            Normalize();
            RaiseChanged();
        }
    }

    [JsonIgnore]
    public int Count => _points.Count;

    /// <summary>
    /// Evaluates the curve at normalised time <paramref name="t"/> using cubic
    /// Hermite interpolation between the surrounding control points. Values are
    /// clamped to the first/last point outside the defined domain.
    /// </summary>
    public float Evaluate(float t)
    {
        int count = _points.Count;
        if (count == 0)
        {
            return 0f;
        }

        if (count == 1)
        {
            return _points[0].Y;
        }

        if (t <= _points[0].X)
        {
            return _points[0].Y;
        }

        int last = count - 1;
        if (t >= _points[last].X)
        {
            return _points[last].Y;
        }

        for (int i = 0; i < last; i++)
        {
            CurvePoint p0 = _points[i];
            CurvePoint p1 = _points[i + 1];
            if (t < p0.X || t > p1.X)
            {
                continue;
            }

            float h = p1.X - p0.X;
            if (h <= 1e-6f)
            {
                return p1.Y;
            }

            float u = (t - p0.X) / h;
            float u2 = u * u;
            float u3 = u2 * u;

            float h00 = 2f * u3 - 3f * u2 + 1f;
            float h10 = u3 - 2f * u2 + u;
            float h01 = -2f * u3 + 3f * u2;
            float h11 = u3 - u2;

            return h00 * p0.Y
                 + h10 * h * p0.RightTangent
                 + h01 * p1.Y
                 + h11 * h * p1.LeftTangent;
        }

        return _points[last].Y;
    }

    /// <summary>
    /// Moves the point at <paramref name="index"/> to (<paramref name="x"/>,
    /// <paramref name="y"/>), clamping both to [0, 1]. Endpoints keep their
    /// X (0 and 1) so the curve always spans the full domain.
    /// </summary>
    public void MovePoint(int index, float x, float y)
    {
        if (index < 0 || index >= _points.Count)
        {
            return;
        }

        CurvePoint p = _points[index];
        if (index == 0)
        {
            x = 0f;
        }
        else if (index == _points.Count - 1)
        {
            x = 1f;
        }
        else
        {
            float min = _points[index - 1].X + 1e-3f;
            float max = _points[index + 1].X - 1e-3f;
            x = Math.Clamp(x, min, max);
        }

        p.X = x;
        p.Y = Math.Clamp(y, 0f, 1f);
        _points[index] = p;
        RaiseChanged();
    }

    /// <summary>Inserts a new point, returning its index in the sorted list.</summary>
    public int AddPoint(float x, float y)
    {
        x = Math.Clamp(x, 0f, 1f);
        y = Math.Clamp(y, 0f, 1f);

        int index = _points.Count;
        for (int i = 0; i < _points.Count; i++)
        {
            if (_points[i].X > x)
            {
                index = i;
                break;
            }
        }

        // Never insert before the first or after the last point.
        if (index == 0)
        {
            index = 1;
        }
        else if (index >= _points.Count)
        {
            index = _points.Count - 1;
        }

        _points.Insert(index, new CurvePoint(x, y));
        Normalize();
        RaiseChanged();
        return index;
    }

    /// <summary>
    /// Removes the interior point at <paramref name="index"/>. The two endpoints
    /// (and a minimum of two points) are always preserved.
    /// </summary>
    public void RemovePoint(int index)
    {
        if (_points.Count <= 2 || index <= 0 || index >= _points.Count - 1)
        {
            return;
        }

        _points.RemoveAt(index);
        RaiseChanged();
    }

    /// <summary>Replaces this curve's points with a copy of <paramref name="other"/>.</summary>
    public void CopyFrom(CurveData other)
    {
        if (other is null || ReferenceEquals(this, other))
        {
            return;
        }

        _points.Clear();
        _points.AddRange(other._points);
        Normalize();
        RaiseChanged();
    }

    public CurveData Clone() => new(_points);

    // ----- Presets ---------------------------------------------------------

    /// <summary>A constant curve (an identity multiplier by default).</summary>
    public static CurveData Flat(float y = 1f) =>
        From(new CurvePoint(0f, y), new CurvePoint(1f, y));

    /// <summary>A straight 0 -> 1 ramp.</summary>
    public static CurveData Linear() =>
        From(new CurvePoint(0f, 0f, 1f, 1f), new CurvePoint(1f, 1f, 1f, 1f));

    /// <summary>Slow start, fast finish (quadratic ease-in).</summary>
    public static CurveData EaseIn() =>
        From(new CurvePoint(0f, 0f, 0f, 0f), new CurvePoint(1f, 1f, 2f, 2f));

    /// <summary>Fast start, slow finish (quadratic ease-out).</summary>
    public static CurveData EaseOut() =>
        From(new CurvePoint(0f, 0f, 2f, 2f), new CurvePoint(1f, 1f, 0f, 0f));

    /// <summary>Grows to a peak at the midpoint then shrinks back to nothing.</summary>
    public static CurveData Bell() =>
        From(
            new CurvePoint(0f, 0f, 0f, 0f),
            new CurvePoint(0.5f, 1f, 0f, 0f),
            new CurvePoint(1f, 0f, 0f, 0f));

    /// <summary>A quick pop to full size followed by a long, slow fade out.</summary>
    public static CurveData FastPopSlowFade() =>
        From(
            new CurvePoint(0f, 0f, 0f, 5f),
            new CurvePoint(0.2f, 1f, 0f, 0f),
            new CurvePoint(1f, 0f, -0.6f, 0f));

    private static CurveData From(params CurvePoint[] points)
    {
        var curve = new CurveData();
        curve._points.Clear();
        curve._points.AddRange(points);
        curve.Normalize();
        return curve;
    }

    private void Normalize()
    {
        _points.Sort((a, b) => a.X.CompareTo(b.X));

        for (int i = 0; i < _points.Count; i++)
        {
            CurvePoint p = _points[i];
            p.X = Math.Clamp(p.X, 0f, 1f);
            p.Y = Math.Clamp(p.Y, 0f, 1f);
            _points[i] = p;
        }

        if (_points.Count == 0)
        {
            _points.Add(new CurvePoint(0f, 1f));
            _points.Add(new CurvePoint(1f, 1f));
        }
        else if (_points.Count == 1)
        {
            CurvePoint only = _points[0];
            if (only.X >= 1f)
            {
                _points.Insert(0, new CurvePoint(0f, only.Y));
            }
            else
            {
                _points.Add(new CurvePoint(1f, only.Y));
            }
        }

        CurvePoint first = _points[0];
        if (first.X > 0f)
        {
            first.X = 0f;
            _points[0] = first;
        }

        CurvePoint last = _points[^1];
        if (last.X < 1f)
        {
            last.X = 1f;
            _points[^1] = last;
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
