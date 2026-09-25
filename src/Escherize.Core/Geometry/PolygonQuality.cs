namespace Escherize.Geometry;

/// <summary>
/// Shape tests used to reject unusable tiles (SPEC §7.3) and to validate generated tiles
/// during template validation (SPEC §8.2).
/// </summary>
public static class PolygonQuality
{
    /// <summary>
    /// The relative distance below which a touch counts as a crossing (SPEC §7.3).
    /// </summary>
    public const double TouchToleranceFraction = 1e-9;

    /// <summary>
    /// Tests whether the closed polygon crosses or touches itself. Segments that share a
    /// vertex are exempt; any other pair closer than
    /// <see cref="TouchToleranceFraction"/> times the diameter counts as a crossing
    /// (SPEC §7.3).
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <returns>True when the polygon is not simple.</returns>
    public static bool SelfIntersects(ReadOnlySpan<Vec2> polygon)
    {
        int n = polygon.Length;
        if (n < 4)
        {
            return false;
        }

        double tolerance = TouchToleranceFraction * PolygonOps.Diameter(polygon);

        for (int i = 0; i < n; i++)
        {
            Vec2 a1 = polygon[i];
            Vec2 a2 = polygon[(i + 1) % n];

            for (int j = i + 1; j < n; j++)
            {
                // Segments that meet at a shared endpoint always touch there.
                bool adjacent = j == i + 1 || (i == 0 && j == n - 1);
                if (adjacent)
                {
                    continue;
                }

                Vec2 b1 = polygon[j];
                Vec2 b2 = polygon[(j + 1) % n];

                if (SegmentDistance(a1, a2, b1, b2) <= tolerance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether the polygon is usable as a tile: simple and positively oriented
    /// (SPEC §7.3).
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <returns>True when the polygon is a valid tile outline.</returns>
    public static bool IsValidTile(ReadOnlySpan<Vec2> polygon) =>
        PolygonOps.SignedArea(polygon) > 0 && !SelfIntersects(polygon);

    /// <summary>
    /// The winding number of the polygon around a point. A non-zero value means the point
    /// lies inside (SPEC §8.2).
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="point">The point.</param>
    /// <returns>The winding number.</returns>
    public static int WindingNumber(ReadOnlySpan<Vec2> polygon, Vec2 point)
    {
        int winding = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vec2 a = polygon[i];
            Vec2 b = polygon[(i + 1) % polygon.Length];

            if (a.Y <= point.Y)
            {
                if (b.Y > point.Y && (b - a).Cross(point - a) > 0)
                {
                    winding++;
                }
            }
            else if (b.Y <= point.Y && (b - a).Cross(point - a) < 0)
            {
                winding--;
            }
        }

        return winding;
    }

    /// <summary>Tests whether a point lies strictly inside the polygon.</summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="point">The point.</param>
    /// <returns>True when the winding number is not zero.</returns>
    public static bool Contains(ReadOnlySpan<Vec2> polygon, Vec2 point) =>
        WindingNumber(polygon, point) != 0;

    /// <summary>The distance from a point to the closed boundary of the polygon.</summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="point">The point.</param>
    /// <returns>The distance.</returns>
    public static double DistanceToBoundary(ReadOnlySpan<Vec2> polygon, Vec2 point)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < polygon.Length; i++)
        {
            double d = ContourSmoothing.PointSegmentDistance(point, polygon[i], polygon[(i + 1) % polygon.Length]);
            if (d < best)
            {
                best = d;
            }
        }

        return best;
    }

    /// <summary>
    /// The smallest distance between a point and a segment whose cyclic index distance is
    /// at least n/10, relative to the square root of the area (SPEC §7.3). This is the
    /// neck width, which decides whether a tile can be produced physically.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <returns>The relative neck width.</returns>
    public static double RelativeNeckWidth(ReadOnlySpan<Vec2> polygon)
    {
        int n = polygon.Length;
        int minimumSeparation = Math.Max(2, n / 10);
        double best = double.PositiveInfinity;

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                int start = j;
                int end = (j + 1) % n;
                if (CyclicDistance(i, start, n) < minimumSeparation || CyclicDistance(i, end, n) < minimumSeparation)
                {
                    continue;
                }

                double d = ContourSmoothing.PointSegmentDistance(polygon[i], polygon[start], polygon[end]);
                if (d < best)
                {
                    best = d;
                }
            }
        }

        double area = Math.Abs(PolygonOps.SignedArea(polygon));
        return area > 0 && double.IsFinite(best) ? best / Math.Sqrt(area) : 0;
    }

    /// <summary>The cyclic distance between two indices.</summary>
    /// <param name="a">The first index.</param>
    /// <param name="b">The second index.</param>
    /// <param name="n">The number of indices.</param>
    /// <returns>The distance, at most n/2.</returns>
    internal static int CyclicDistance(int a, int b, int n)
    {
        int d = Math.Abs(a - b);
        return Math.Min(d, n - d);
    }

    /// <summary>The smallest distance between two line segments.</summary>
    /// <param name="a1">The start of the first segment.</param>
    /// <param name="a2">The end of the first segment.</param>
    /// <param name="b1">The start of the second segment.</param>
    /// <param name="b2">The end of the second segment.</param>
    /// <returns>Zero when they cross, otherwise the smallest distance.</returns>
    internal static double SegmentDistance(Vec2 a1, Vec2 a2, Vec2 b1, Vec2 b2)
    {
        Vec2 da = a2 - a1;
        Vec2 db = b2 - b1;
        double denominator = da.Cross(db);

        if (Math.Abs(denominator) > 1e-300)
        {
            double t = (b1 - a1).Cross(db) / denominator;
            double s = (b1 - a1).Cross(da) / denominator;
            if (t is >= 0 and <= 1 && s is >= 0 and <= 1)
            {
                return 0;
            }
        }

        double best = ContourSmoothing.PointSegmentDistance(b1, a1, a2);
        best = Math.Min(best, ContourSmoothing.PointSegmentDistance(b2, a1, a2));
        best = Math.Min(best, ContourSmoothing.PointSegmentDistance(a1, b1, b2));
        best = Math.Min(best, ContourSmoothing.PointSegmentDistance(a2, b1, b2));
        return best;
    }
}
