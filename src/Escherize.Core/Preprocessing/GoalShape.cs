using Escherize.Geometry;

namespace Escherize.Preprocessing;

/// <summary>
/// The normalised search target (SPEC §3, §4.5): n points with zero mean and unit
/// coordinate norm, together with the transform back to input units.
/// </summary>
public sealed class GoalShape
{
    private readonly Vec2[] _points;
    private readonly Vec2[] _reversedPoints;
    private readonly double[] _w;
    private readonly double[] _wReversed;

    private GoalShape(Vec2[] points, Vec2 centroid, double scale)
    {
        _points = points;
        _reversedPoints = PolygonOps.Reverse(points);
        _w = ToCoordinateVector(points);
        _wReversed = ToCoordinateVector(_reversedPoints);
        Centroid = centroid;
        Scale = scale;
    }

    /// <summary>The number of points, n.</summary>
    public int PointCount => _points.Length;

    /// <summary>The normalised points in boundary order, counter-clockwise.</summary>
    public ReadOnlySpan<Vec2> Points => _points;

    /// <summary>
    /// The normalised points in reverse order, W_rev[t] = W[(n - t) mod n] (SPEC §4.5).
    /// Running the search on both orientations covers mirrored templates.
    /// </summary>
    public ReadOnlySpan<Vec2> ReversedPoints => _reversedPoints;

    /// <summary>The coordinate vector w of length 2n: w[t] = x_t and w[n + t] = y_t (SPEC §3).</summary>
    public ReadOnlySpan<double> W => _w;

    /// <summary>The coordinate vector of <see cref="ReversedPoints"/>.</summary>
    public ReadOnlySpan<double> WReversed => _wReversed;

    /// <summary>The point average that was subtracted during normalisation, in input units.</summary>
    public Vec2 Centroid { get; }

    /// <summary>The factor the centred points were divided by, in input units.</summary>
    public double Scale { get; }

    /// <summary>Transforms a normalised point back to input units.</summary>
    /// <param name="point">The normalised point.</param>
    /// <returns>The point in input units.</returns>
    public Vec2 Denormalize(Vec2 point) => (point * Scale) + Centroid;

    /// <summary>
    /// Normalises a resampled contour: subtract the point average and divide by the norm
    /// of the centred coordinate vector, so that the result has ||w|| = 1 (SPEC §3).
    /// </summary>
    /// <param name="contour">The contour, already resampled to n points and positively oriented.</param>
    /// <returns>The normalised goal shape.</returns>
    /// <exception cref="ArgumentException">The contour has fewer than three points or zero extent.</exception>
    public static GoalShape FromResampledContour(ReadOnlySpan<Vec2> contour)
    {
        if (contour.Length < 3)
        {
            throw new ArgumentException("A goal shape needs at least three points.", nameof(contour));
        }

        Vec2 centroid = PolygonOps.PointAverage(contour);

        double sumSquares = 0;
        for (int i = 0; i < contour.Length; i++)
        {
            sumSquares += (contour[i] - centroid).LengthSquared;
        }

        double scale = Math.Sqrt(sumSquares);
        if (scale <= 0)
        {
            throw new ArgumentException("The contour has zero extent.", nameof(contour));
        }

        var normalized = new Vec2[contour.Length];
        for (int i = 0; i < contour.Length; i++)
        {
            normalized[i] = (contour[i] - centroid) / scale;
        }

        return new GoalShape(normalized, centroid, scale);
    }

    /// <summary>Packs points into the 2n coordinate vector layout of SPEC §3.</summary>
    /// <param name="points">The points.</param>
    /// <returns>The coordinate vector.</returns>
    private static double[] ToCoordinateVector(Vec2[] points)
    {
        int n = points.Length;
        var vector = new double[2 * n];
        for (int t = 0; t < n; t++)
        {
            vector[t] = points[t].X;
            vector[n + t] = points[t].Y;
        }

        return vector;
    }
}
