using Escherize.Geometry;

namespace Escherize.Preprocessing;

/// <summary>Options for the preprocessing pipeline (SPEC §4.4, §4.5).</summary>
public sealed record PreprocessOptions
{
    /// <summary>The number of goal points n, the <c>--n</c> flag. The default is 64.</summary>
    public int PointCount { get; init; } = 64;

    /// <summary>
    /// The highest retained harmonic H, the <c>--smooth</c> flag. The default is 24;
    /// zero disables Fourier smoothing.
    /// </summary>
    public int SmoothHarmonics { get; init; } = ContourSmoothing.DefaultHarmonics;

    /// <summary>
    /// The Douglas-Peucker tolerance as a fraction of the diameter, the <c>--dp</c> flag.
    /// When set it replaces Fourier smoothing.
    /// </summary>
    public double? DouglasPeuckerTolerance { get; init; }

    /// <summary>The number of points the contour is resampled to before the transform (SPEC §4.4).</summary>
    public int TransformPointCount { get; init; } = ContourSmoothing.DefaultTransformPointCount;
}
