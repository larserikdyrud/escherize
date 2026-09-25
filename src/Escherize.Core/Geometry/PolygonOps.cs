namespace Escherize.Geometry;

/// <summary>
/// Operations on closed polygons. A polygon is an array of distinct points in boundary
/// order; the closing edge from the last point back to the first is implicit.
/// </summary>
public static class PolygonOps
{
    /// <summary>
    /// The signed area of a closed polygon. Positive means counter-clockwise in the
    /// y-up convention (SPEC §3).
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <returns>The signed area.</returns>
    public static double SignedArea(ReadOnlySpan<Vec2> polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vec2 a = polygon[i];
            Vec2 b = polygon[(i + 1) % polygon.Length];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum * 0.5;
    }

    /// <summary>The total length of the closed boundary.</summary>
    /// <param name="polygon">The polygon.</param>
    /// <returns>The perimeter.</returns>
    public static double Perimeter(ReadOnlySpan<Vec2> polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            sum += polygon[i].DistanceTo(polygon[(i + 1) % polygon.Length]);
        }

        return sum;
    }

    /// <summary>
    /// The mean of the polygon points. This is the point average used for normalisation
    /// (SPEC §3), not the area centroid.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <returns>The point average.</returns>
    public static Vec2 PointAverage(ReadOnlySpan<Vec2> polygon)
    {
        double sx = 0;
        double sy = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            sx += polygon[i].X;
            sy += polygon[i].Y;
        }

        return new Vec2(sx / polygon.Length, sy / polygon.Length);
    }

    /// <summary>Returns the polygon in reverse boundary order, keeping the first point first.</summary>
    /// <param name="polygon">The polygon.</param>
    /// <returns>The reversed polygon.</returns>
    public static Vec2[] Reverse(ReadOnlySpan<Vec2> polygon)
    {
        int n = polygon.Length;
        var result = new Vec2[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = polygon[(n - i) % n];
        }

        return result;
    }

    /// <summary>
    /// Returns the polygon oriented counter-clockwise (signed area &gt; 0), reversing it
    /// if necessary (SPEC §3).
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <returns>A positively oriented polygon.</returns>
    public static Vec2[] EnsurePositiveOrientation(ReadOnlySpan<Vec2> polygon) =>
        SignedArea(polygon) >= 0 ? polygon.ToArray() : Reverse(polygon);

    /// <summary>Mirrors the polygon about the x axis, converting y-down to y-up (SPEC §3).</summary>
    /// <param name="polygon">The polygon.</param>
    /// <returns>The mirrored polygon.</returns>
    public static Vec2[] FlipY(ReadOnlySpan<Vec2> polygon)
    {
        var result = new Vec2[polygon.Length];
        for (int i = 0; i < polygon.Length; i++)
        {
            result[i] = new Vec2(polygon[i].X, -polygon[i].Y);
        }

        return result;
    }

    /// <summary>
    /// Removes points that coincide with their predecessor, cyclically, within
    /// <paramref name="tolerance"/> (SPEC §4.2).
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="tolerance">The absolute distance below which two points count as equal.</param>
    /// <returns>The polygon without consecutive duplicates.</returns>
    public static Vec2[] RemoveConsecutiveDuplicates(ReadOnlySpan<Vec2> polygon, double tolerance)
    {
        if (polygon.Length == 0)
        {
            return [];
        }

        var kept = new List<Vec2>(polygon.Length) { polygon[0] };
        for (int i = 1; i < polygon.Length; i++)
        {
            if (polygon[i].DistanceTo(kept[^1]) > tolerance)
            {
                kept.Add(polygon[i]);
            }
        }

        // The closing edge can be degenerate too.
        while (kept.Count > 1 && kept[^1].DistanceTo(kept[0]) <= tolerance)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return [.. kept];
    }

    /// <summary>
    /// The convex hull in counter-clockwise order, computed with the monotone chain method.
    /// </summary>
    /// <param name="points">The input points; the order does not matter.</param>
    /// <returns>The hull vertices, counter-clockwise.</returns>
    public static Vec2[] ConvexHull(ReadOnlySpan<Vec2> points)
    {
        if (points.Length < 3)
        {
            return points.ToArray();
        }

        Vec2[] sorted = points.ToArray();
        Array.Sort(sorted, static (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

        var hull = new Vec2[2 * sorted.Length];
        int count = 0;

        for (int i = 0; i < sorted.Length; i++)
        {
            while (count >= 2 && (hull[count - 1] - hull[count - 2]).Cross(sorted[i] - hull[count - 2]) <= 0)
            {
                count--;
            }

            hull[count++] = sorted[i];
        }

        int lowerHullSize = count + 1;
        for (int i = sorted.Length - 2; i >= 0; i--)
        {
            while (count >= lowerHullSize && (hull[count - 1] - hull[count - 2]).Cross(sorted[i] - hull[count - 2]) <= 0)
            {
                count--;
            }

            hull[count++] = sorted[i];
        }

        return hull[..Math.Max(count - 1, 1)];
    }

    /// <summary>
    /// The diameter: the largest distance between any two points. Several tolerances in
    /// the specification are expressed as a fraction of this value.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <returns>The diameter, or zero for fewer than two points.</returns>
    public static double Diameter(ReadOnlySpan<Vec2> points)
    {
        if (points.Length < 2)
        {
            return 0;
        }

        Vec2[] hull = ConvexHull(points);
        double best = 0;
        for (int i = 0; i < hull.Length; i++)
        {
            for (int j = i + 1; j < hull.Length; j++)
            {
                double d = (hull[i] - hull[j]).LengthSquared;
                if (d > best)
                {
                    best = d;
                }
            }
        }

        return Math.Sqrt(best);
    }

    /// <summary>
    /// Resamples the closed boundary to <paramref name="count"/> points spaced at equal
    /// arc length, starting at the first input point (SPEC §4.4, §4.5).
    /// </summary>
    /// <param name="polygon">The polygon to resample.</param>
    /// <param name="count">The number of output points.</param>
    /// <returns>The resampled polygon.</returns>
    /// <exception cref="ArgumentException">The polygon has fewer than three points or zero perimeter.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is less than three.</exception>
    public static Vec2[] Resample(ReadOnlySpan<Vec2> polygon, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 3);
        if (polygon.Length < 3)
        {
            throw new ArgumentException("A polygon needs at least three points.", nameof(polygon));
        }

        int n = polygon.Length;
        var cumulative = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            cumulative[i + 1] = cumulative[i] + polygon[i].DistanceTo(polygon[(i + 1) % n]);
        }

        double total = cumulative[n];
        if (total <= 0)
        {
            throw new ArgumentException("The polygon has zero perimeter.", nameof(polygon));
        }

        double step = total / count;
        var result = new Vec2[count];
        result[0] = polygon[0];

        int segment = 0;
        for (int i = 1; i < count; i++)
        {
            double target = i * step;
            while (segment < n - 1 && cumulative[segment + 1] <= target)
            {
                segment++;
            }

            double segmentLength = cumulative[segment + 1] - cumulative[segment];
            double t = segmentLength > 0 ? (target - cumulative[segment]) / segmentLength : 0;
            result[i] = Vec2.Lerp(polygon[segment], polygon[(segment + 1) % n], t);
        }

        return result;
    }
}
