using Escherize.Geometry;

namespace Escherize.Preprocessing;

/// <summary>
/// Turns a raw contour into a normalised goal shape: duplicate removal and orientation
/// (SPEC §4.2), smoothing (SPEC §4.4), resampling and normalisation (SPEC §4.5).
/// </summary>
public static class Preprocessor
{
    /// <summary>The relative distance below which two consecutive contour points count as equal (SPEC §4.2).</summary>
    public const double DuplicateToleranceFraction = 1e-12;

    /// <summary>Runs the full preprocessing pipeline.</summary>
    /// <param name="contour">The raw closed contour, in input units and y-up.</param>
    /// <param name="options">The preprocessing options.</param>
    /// <returns>The normalised goal shape.</returns>
    /// <exception cref="ArgumentException">The contour is degenerate.</exception>
    public static GoalShape Build(ReadOnlySpan<Vec2> contour, PreprocessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Vec2[] cleaned = Clean(contour);

        Vec2[] smoothed = options.DouglasPeuckerTolerance is { } dpTolerance
            ? ContourSmoothing.DouglasPeucker(cleaned, dpTolerance)
            : options.SmoothHarmonics > 0
                ? ContourSmoothing.FourierLowPass(cleaned, options.SmoothHarmonics, options.TransformPointCount)
                : cleaned;

        // Smoothing can fold the contour back on itself, so orientation is re-established.
        Vec2[] oriented = PolygonOps.EnsurePositiveOrientation(smoothed);
        Vec2[] resampled = PolygonOps.Resample(oriented, options.PointCount);
        return GoalShape.FromResampledContour(resampled);
    }

    /// <summary>
    /// Removes consecutive duplicates and orients the contour counter-clockwise (SPEC §4.2).
    /// </summary>
    /// <param name="contour">The raw contour.</param>
    /// <returns>The cleaned contour.</returns>
    /// <exception cref="ArgumentException">Fewer than three distinct points remain.</exception>
    public static Vec2[] Clean(ReadOnlySpan<Vec2> contour)
    {
        double diameter = PolygonOps.Diameter(contour);
        Vec2[] distinct = PolygonOps.RemoveConsecutiveDuplicates(contour, DuplicateToleranceFraction * diameter);
        if (distinct.Length < 3)
        {
            throw new ArgumentException("The contour has fewer than three distinct points.", nameof(contour));
        }

        return PolygonOps.EnsurePositiveOrientation(distinct);
    }
}
