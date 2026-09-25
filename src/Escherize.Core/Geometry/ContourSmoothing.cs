namespace Escherize.Geometry;

/// <summary>
/// Contour smoothing (SPEC §4.4): an elliptic Fourier low pass filter by default, with
/// Douglas-Peucker simplification as an alternative.
/// </summary>
public static class ContourSmoothing
{
    /// <summary>The number of points the contour is resampled to before the transform (SPEC §4.4).</summary>
    public const int DefaultTransformPointCount = 2048;

    /// <summary>The default number of retained harmonics (SPEC §4.4).</summary>
    public const int DefaultHarmonics = 24;

    /// <summary>
    /// Resamples the contour to <paramref name="transformPointCount"/> points at equal arc
    /// length, applies a discrete Fourier transform to z = x + iy, keeps the harmonics with
    /// |h| &lt;= <paramref name="harmonics"/> and transforms back (SPEC §4.4).
    /// </summary>
    /// <param name="polygon">The closed contour.</param>
    /// <param name="harmonics">The highest retained harmonic; zero or less disables filtering.</param>
    /// <param name="transformPointCount">The number of points used for the transform.</param>
    /// <returns>The smoothed contour, with <paramref name="transformPointCount"/> points.</returns>
    public static Vec2[] FourierLowPass(
        ReadOnlySpan<Vec2> polygon,
        int harmonics = DefaultHarmonics,
        int transformPointCount = DefaultTransformPointCount)
    {
        Vec2[] samples = PolygonOps.Resample(polygon, transformPointCount);
        if (harmonics <= 0)
        {
            return samples;
        }

        int n = samples.Length;
        int h = Math.Min(harmonics, n / 2);

        // Coefficients c_k for k = -h .. h, stored at index k + h.
        int coefficientCount = (2 * h) + 1;
        var real = new double[coefficientCount];
        var imaginary = new double[coefficientCount];

        double twoPiOverN = 2.0 * Math.PI / n;
        for (int k = -h; k <= h; k++)
        {
            double sumReal = 0;
            double sumImaginary = 0;
            for (int t = 0; t < n; t++)
            {
                double angle = -twoPiOverN * k * t;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                sumReal += (samples[t].X * cos) - (samples[t].Y * sin);
                sumImaginary += (samples[t].X * sin) + (samples[t].Y * cos);
            }

            real[k + h] = sumReal / n;
            imaginary[k + h] = sumImaginary / n;
        }

        var result = new Vec2[n];
        for (int t = 0; t < n; t++)
        {
            double sumReal = 0;
            double sumImaginary = 0;
            for (int k = -h; k <= h; k++)
            {
                double angle = twoPiOverN * k * t;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                sumReal += (real[k + h] * cos) - (imaginary[k + h] * sin);
                sumImaginary += (real[k + h] * sin) + (imaginary[k + h] * cos);
            }

            result[t] = new Vec2(sumReal, sumImaginary);
        }

        return result;
    }

    /// <summary>
    /// Simplifies the closed contour with the Douglas-Peucker method (SPEC §4.4). The
    /// tolerance is a fraction of the contour diameter.
    /// </summary>
    /// <param name="polygon">The closed contour.</param>
    /// <param name="toleranceFraction">The tolerance as a fraction of the diameter.</param>
    /// <returns>The simplified contour.</returns>
    public static Vec2[] DouglasPeucker(ReadOnlySpan<Vec2> polygon, double toleranceFraction)
    {
        if (polygon.Length < 4 || toleranceFraction <= 0)
        {
            return polygon.ToArray();
        }

        double tolerance = toleranceFraction * PolygonOps.Diameter(polygon);

        // A closed curve is split at two far apart points so that each half is an open
        // polyline, which is what the recursion expects.
        int start = 0;
        int opposite = 0;
        double best = -1;
        for (int i = 1; i < polygon.Length; i++)
        {
            double d = polygon[i].DistanceTo(polygon[start]);
            if (d > best)
            {
                best = d;
                opposite = i;
            }
        }

        // Each half contributes its first point and leaves its last point to the other half.
        var result = new List<Vec2>(polygon.Length);
        AppendSimplified(polygon, start, opposite, tolerance, result);
        AppendSimplified(polygon, opposite, polygon.Length, tolerance, result, wrapTo: start);

        return [.. result];
    }

    /// <summary>
    /// Appends the simplified polyline from <paramref name="first"/> up to but excluding
    /// <paramref name="last"/>, keeping the first point and dropping the last.
    /// </summary>
    /// <param name="polygon">The full contour.</param>
    /// <param name="first">The index of the first point of the chain.</param>
    /// <param name="last">The index one past the last point of the chain.</param>
    /// <param name="tolerance">The absolute distance tolerance.</param>
    /// <param name="output">The list the kept points are appended to.</param>
    /// <param name="wrapTo">The index the chain closes onto, for the second half.</param>
    private static void AppendSimplified(
        ReadOnlySpan<Vec2> polygon,
        int first,
        int last,
        double tolerance,
        List<Vec2> output,
        int wrapTo = -1)
    {
        int count = last - first + 1;
        var chain = new Vec2[count];
        for (int i = 0; i < count - 1; i++)
        {
            chain[i] = polygon[first + i];
        }

        chain[count - 1] = wrapTo >= 0 ? polygon[wrapTo] : polygon[last];

        var keep = new bool[count];
        keep[0] = true;
        keep[count - 1] = true;
        Simplify(chain, 0, count - 1, tolerance, keep);

        for (int i = 0; i < count - 1; i++)
        {
            if (keep[i])
            {
                output.Add(chain[i]);
            }
        }
    }

    /// <summary>The recursive Douglas-Peucker step.</summary>
    /// <param name="chain">The open polyline.</param>
    /// <param name="first">The index of the first point of the current span.</param>
    /// <param name="last">The index of the last point of the current span.</param>
    /// <param name="tolerance">The absolute distance tolerance.</param>
    /// <param name="keep">The flags marking which points survive.</param>
    private static void Simplify(ReadOnlySpan<Vec2> chain, int first, int last, double tolerance, bool[] keep)
    {
        if (last <= first + 1)
        {
            return;
        }

        double maxDistance = -1;
        int split = -1;
        for (int i = first + 1; i < last; i++)
        {
            double d = PointSegmentDistance(chain[i], chain[first], chain[last]);
            if (d > maxDistance)
            {
                maxDistance = d;
                split = i;
            }
        }

        if (maxDistance <= tolerance || split < 0)
        {
            return;
        }

        keep[split] = true;
        Simplify(chain, first, split, tolerance, keep);
        Simplify(chain, split, last, tolerance, keep);
    }

    /// <summary>The distance from a point to a line segment.</summary>
    /// <param name="point">The point.</param>
    /// <param name="a">The start of the segment.</param>
    /// <param name="b">The end of the segment.</param>
    /// <returns>The distance.</returns>
    internal static double PointSegmentDistance(Vec2 point, Vec2 a, Vec2 b)
    {
        Vec2 ab = b - a;
        double lengthSquared = ab.LengthSquared;
        if (lengthSquared <= 0)
        {
            return point.DistanceTo(a);
        }

        double t = Math.Clamp((point - a).Dot(ab) / lengthSquared, 0, 1);
        return point.DistanceTo(a + (ab * t));
    }
}
