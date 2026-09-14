namespace Structed.Inkwell.Mapping;

/// <summary>A point in map space.</summary>
public readonly record struct MapPoint(double X, double Y)
{
    public static MapPoint operator +(MapPoint a, MapPoint b) => new(a.X + b.X, a.Y + b.Y);

    public static MapPoint operator -(MapPoint a, MapPoint b) => new(a.X - b.X, a.Y - b.Y);

    public static MapPoint operator *(MapPoint a, double scale) => new(a.X * scale, a.Y * scale);

    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public double DistanceTo(MapPoint other) => (this - other).Length;

    public MapPoint Normalised()
    {
        double length = Length;
        return length < 1e-9 ? new MapPoint(0, 0) : new MapPoint(X / length, Y / length);
    }

    /// <summary>Rotated a quarter turn, which gives the offset direction for a road's two sides.</summary>
    public MapPoint Perpendicular() => new(-Y, X);

    public MapPoint Lerp(MapPoint other, double t) =>
        new(X + ((other.X - X) * t), Y + ((other.Y - Y) * t));

    public static MapPoint FromAngle(double radians, double length = 1) =>
        new(Math.Cos(radians) * length, Math.Sin(radians) * length);
}

/// <summary>An axis-aligned bounding box.</summary>
public readonly record struct MapBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;

    public double Height => MaxY - MinY;

    public MapPoint Centre => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public MapBounds Expanded(double margin) =>
        new(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);
}

/// <summary>A closed polygon in map space.</summary>
public sealed class MapPolygon
{
    private readonly MapPoint[] _points;

    public MapPolygon(IEnumerable<MapPoint> points)
    {
        _points = [.. points];

        if (_points.Length < 3)
        {
            throw new ArgumentException("A polygon needs at least three points.", nameof(points));
        }

        Bounds = ComputeBounds(_points);
    }

    public IReadOnlyList<MapPoint> Points => _points;

    public MapBounds Bounds { get; }

    public MapPoint Centre => Bounds.Centre;

    /// <summary>Whether the point lies inside, by the even-odd ray casting rule.</summary>
    public bool Contains(MapPoint point)
    {
        bool inside = false;

        for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
        {
            MapPoint a = _points[i];
            MapPoint b = _points[j];

            bool straddles = a.Y > point.Y != b.Y > point.Y;
            if (!straddles)
            {
                continue;
            }

            double crossingX = ((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X;
            if (point.X < crossingX)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>The shortest distance from the point to the polygon's outline.</summary>
    public double DistanceToEdge(MapPoint point)
    {
        double closest = double.MaxValue;

        for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
        {
            closest = Math.Min(closest, DistanceToSegment(point, _points[j], _points[i]));
        }

        return closest;
    }

    /// <summary>
    /// Whether the point is inside and at least <paramref name="margin"/> clear of the outline,
    /// which is what keeps buildings from straddling the wall of their host object.
    /// </summary>
    public bool ContainsWithMargin(MapPoint point, double margin) =>
        Contains(point) && DistanceToEdge(point) >= margin;

    public static double DistanceToSegment(MapPoint point, MapPoint a, MapPoint b)
    {
        MapPoint ab = b - a;
        double lengthSquared = (ab.X * ab.X) + (ab.Y * ab.Y);

        if (lengthSquared < 1e-9)
        {
            return point.DistanceTo(a);
        }

        MapPoint ap = point - a;
        double t = Math.Clamp(((ap.X * ab.X) + (ap.Y * ab.Y)) / lengthSquared, 0, 1);

        return point.DistanceTo(a + (ab * t));
    }

    /// <summary>
    /// Whether two segments cross, ignoring contact at their endpoints.
    /// </summary>
    /// <remarks>
    /// Endpoint contact is excluded deliberately: roads are expected to meet at junctions, and
    /// only a genuine crossing partway along a road should be rejected.
    /// </remarks>
    public static bool SegmentsCross(MapPoint a1, MapPoint a2, MapPoint b1, MapPoint b2)
    {
        const double epsilon = 1e-6;

        if (SharesEndpoint(a1, a2, b1, b2, epsilon))
        {
            return false;
        }

        double d1 = Cross(b2 - b1, a1 - b1);
        double d2 = Cross(b2 - b1, a2 - b1);
        double d3 = Cross(a2 - a1, b1 - a1);
        double d4 = Cross(a2 - a1, b2 - a1);

        return ((d1 > epsilon && d2 < -epsilon) || (d1 < -epsilon && d2 > epsilon)) &&
               ((d3 > epsilon && d4 < -epsilon) || (d3 < -epsilon && d4 > epsilon));
    }

    private static bool SharesEndpoint(MapPoint a1, MapPoint a2, MapPoint b1, MapPoint b2, double epsilon) =>
        a1.DistanceTo(b1) < epsilon || a1.DistanceTo(b2) < epsilon ||
        a2.DistanceTo(b1) < epsilon || a2.DistanceTo(b2) < epsilon;

    private static double Cross(MapPoint a, MapPoint b) => (a.X * b.Y) - (a.Y * b.X);

    private static MapBounds ComputeBounds(MapPoint[] points)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (MapPoint p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return new MapBounds(minX, minY, maxX, maxY);
    }
}
