using System.Globalization;
using System.Text;
using Structed.Inkwell.Mapping;
using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Rendering;

/// <summary>
/// Draws lines the way a pen does: slightly off, and twice.
/// </summary>
/// <remarks>
/// <para>
/// Each edge becomes a cubic Bézier whose control points are displaced from the true line, then is
/// drawn a second time with roughly half the displacement. The contrast between the two passes is
/// what reads as ink rather than as a wobbly vector.
/// </para>
/// <para>
/// Endpoints are never displaced. On a map the walls of adjacent buildings and the ends of meeting
/// roads must still touch, and moving endpoints is what makes hand-drawn rendering fall apart.
/// </para>
/// </remarks>
public sealed class RoughPen(IRandomSource source, double roughness = 1.0)
{
    private readonly IRandomSource _source =
        source ?? throw new ArgumentNullException(nameof(source));

    private readonly double _roughness = Math.Max(0, roughness);

    /// <summary>A value in <c>[-1, 1]</c>.</summary>
    private double Signed() => ((_source.NextUInt32(2001) / 1000.0) - 1.0);

    /// <summary>
    /// The displacement to apply on a line of the given length.
    /// </summary>
    /// <remarks>
    /// Damped on long lines, which would otherwise bow alarmingly, and clamped on short ones so
    /// that the many tiny buildings on a mouse-scale map do not dissolve.
    /// </remarks>
    private double Offset(double length)
    {
        double scale = length > 200 ? 0.45 : length > 90 ? 0.7 : 1.0;
        double maximum = Math.Min(2.4, Math.Max(0.4, length / 9));

        return Signed() * maximum * scale * _roughness;
    }

    /// <summary>Emits one hand-drawn pass over a straight line.</summary>
    public string Line(MapPoint from, MapPoint to, double amplitude = 1.0)
    {
        double length = from.DistanceTo(to);

        MapPoint c1 = from.Lerp(to, 0.25 + (Signed() * 0.06));
        MapPoint c2 = from.Lerp(to, 0.75 + (Signed() * 0.06));

        c1 = new MapPoint(c1.X + (Offset(length) * amplitude), c1.Y + (Offset(length) * amplitude));
        c2 = new MapPoint(c2.X + (Offset(length) * amplitude), c2.Y + (Offset(length) * amplitude));

        return $"M {N(from.X)} {N(from.Y)} C {N(c1.X)} {N(c1.Y)}, {N(c2.X)} {N(c2.Y)}, {N(to.X)} {N(to.Y)}";
    }

    /// <summary>Emits the two passes that make a line look inked.</summary>
    public IEnumerable<string> DoubleLine(MapPoint from, MapPoint to)
    {
        yield return Line(from, to);
        yield return Line(from, to, 0.5);
    }

    /// <summary>Emits a hand-drawn closed outline through the given points.</summary>
    public string ClosedPath(IReadOnlyList<MapPoint> points, double amplitude = 1.0)
    {
        if (points.Count < 2)
        {
            return "";
        }

        StringBuilder path = new();
        path.Append($"M {N(points[0].X)} {N(points[0].Y)}");

        for (int i = 0; i < points.Count; i++)
        {
            MapPoint from = points[i];
            MapPoint to = points[(i + 1) % points.Count];
            double length = from.DistanceTo(to);

            MapPoint c1 = from.Lerp(to, 0.3);
            MapPoint c2 = from.Lerp(to, 0.7);

            c1 = new MapPoint(c1.X + (Offset(length) * amplitude), c1.Y + (Offset(length) * amplitude));
            c2 = new MapPoint(c2.X + (Offset(length) * amplitude), c2.Y + (Offset(length) * amplitude));

            path.Append($" C {N(c1.X)} {N(c1.Y)}, {N(c2.X)} {N(c2.Y)}, {N(to.X)} {N(to.Y)}");
        }

        path.Append(" Z");
        return path.ToString();
    }

    /// <summary>Emits a hand-drawn open polyline.</summary>
    public string OpenPath(IReadOnlyList<MapPoint> points, double amplitude = 1.0)
    {
        if (points.Count < 2)
        {
            return "";
        }

        StringBuilder path = new();
        path.Append($"M {N(points[0].X)} {N(points[0].Y)}");

        for (int i = 0; i < points.Count - 1; i++)
        {
            MapPoint from = points[i];
            MapPoint to = points[i + 1];
            double length = from.DistanceTo(to);

            MapPoint c1 = from.Lerp(to, 0.3);
            MapPoint c2 = from.Lerp(to, 0.7);

            c1 = new MapPoint(c1.X + (Offset(length) * amplitude), c1.Y + (Offset(length) * amplitude));
            c2 = new MapPoint(c2.X + (Offset(length) * amplitude), c2.Y + (Offset(length) * amplitude));

            path.Append($" C {N(c1.X)} {N(c1.Y)}, {N(c2.X)} {N(c2.Y)}, {N(to.X)} {N(to.Y)}");
        }

        return path.ToString();
    }

    /// <summary>
    /// Fills a polygon with parallel hatching, returned as individual strokes.
    /// </summary>
    /// <remarks>
    /// A scanline sweep at a deliberately odd angle, taking each consecutive pair of crossings so
    /// concave shapes and holes fall out of the even-odd rule for free.
    /// </remarks>
    public IEnumerable<string> Hatch(IReadOnlyList<MapPoint> points, double spacing, double angleDegrees = -41)
    {
        if (points.Count < 3)
        {
            yield break;
        }

        double angle = angleDegrees * Math.PI / 180;
        double cos = Math.Cos(-angle);
        double sin = Math.Sin(-angle);

        MapPoint Rotate(MapPoint p) => new((p.X * cos) - (p.Y * sin), (p.X * sin) + (p.Y * cos));
        MapPoint Unrotate(MapPoint p) => new((p.X * cos) + (p.Y * sin), (-p.X * sin) + (p.Y * cos));

        List<MapPoint> rotated = [.. points.Select(Rotate)];

        double minY = rotated.Min(p => p.Y);
        double maxY = rotated.Max(p => p.Y);

        for (double y = minY + (spacing / 2); y < maxY; y += spacing)
        {
            List<double> crossings = [];

            for (int i = 0, j = rotated.Count - 1; i < rotated.Count; j = i++)
            {
                MapPoint a = rotated[i];
                MapPoint b = rotated[j];

                if (a.Y > y != b.Y > y)
                {
                    crossings.Add(((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X);
                }
            }

            crossings.Sort();

            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                if (crossings[i + 1] - crossings[i] < 1)
                {
                    continue;
                }

                yield return Line(
                    Unrotate(new MapPoint(crossings[i], y)),
                    Unrotate(new MapPoint(crossings[i + 1], y)),
                    0.6);
            }
        }
    }

    /// <summary>A terse alias for <see cref="SvgNumber.Format"/>, used heavily below.</summary>
    internal static string N(double value) => SvgNumber.Format(value);
}
