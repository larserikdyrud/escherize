using Escherize.Geometry;
using Xunit;

namespace Escherize.Tests;

/// <summary>Resampling, orientation and smoothing tests (SPEC §8.1).</summary>
public sealed class GeometryTests
{
    /// <summary>
    /// Resampling places points at equal arc length along the original boundary, within
    /// 1e-9 (SPEC §8.1). The arc length of each output point is recovered by locating it
    /// on the source polyline.
    /// </summary>
    /// <param name="count">The number of output points.</param>
    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(101)]
    public void ResampleSpacesPointsAtEqualArcLength(int count)
    {
        Vec2[] source = TestShapes.LShape();
        Vec2[] resampled = PolygonOps.Resample(source, count);

        Assert.Equal(count, resampled.Length);

        double perimeter = PolygonOps.Perimeter(source);
        double expectedStep = perimeter / count;

        for (int i = 0; i < count; i++)
        {
            double actual = ArcLengthOf(source, resampled[i]);
            Assert.Equal(i * expectedStep, actual, 1e-9);
        }
    }

    /// <summary>
    /// On a shape where no sample straddles a corner, equal arc length also means equal
    /// chord length, which is a direct check of the spacing.
    /// </summary>
    [Fact]
    public void ResampleOfSquareHasUniformChords()
    {
        Vec2[] resampled = PolygonOps.Resample(TestShapes.Square(), 40);

        for (int i = 0; i < resampled.Length; i++)
        {
            double chord = resampled[i].DistanceTo(resampled[(i + 1) % resampled.Length]);
            Assert.Equal(0.1, chord, 1e-9);
        }
    }

    /// <summary>Resampling keeps the first input point as the first output point (SPEC §4.5).</summary>
    [Fact]
    public void ResampleKeepsTheStartPoint()
    {
        Vec2[] source = TestShapes.LShape();
        Vec2[] resampled = PolygonOps.Resample(source, 33);
        Assert.Equal(source[0], resampled[0]);
    }

    /// <summary>Import orients every polygon counter-clockwise, so the signed area is positive (SPEC §8.1).</summary>
    [Fact]
    public void ImportProducesPositiveSignedArea()
    {
        Vec2[] clockwise = PolygonOps.Reverse(TestShapes.LShape());
        Assert.True(PolygonOps.SignedArea(clockwise) < 0);

        Vec2[] oriented = PolygonOps.EnsurePositiveOrientation(clockwise);
        Assert.True(PolygonOps.SignedArea(oriented) > 0);
        Assert.Equal(Math.Abs(PolygonOps.SignedArea(clockwise)), PolygonOps.SignedArea(oriented), 1e-12);
    }

    /// <summary>Reversing twice returns the original polygon.</summary>
    [Fact]
    public void ReverseIsAnInvolution()
    {
        Vec2[] source = TestShapes.LShape();
        Vec2[] twice = PolygonOps.Reverse(PolygonOps.Reverse(source));
        Assert.Equal(source, twice);
    }

    /// <summary>Smoothing leaves a circle unchanged within 1e-6 (SPEC §8.1).</summary>
    [Fact]
    public void SmoothingLeavesACircleUnchanged()
    {
        const int Count = 2048;
        Vec2[] circle = TestShapes.Circle(Count, radius: 2.5, center: new Vec2(1.25, -0.75));
        Vec2[] smoothed = ContourSmoothing.FourierLowPass(circle, harmonics: 24, transformPointCount: Count);

        Assert.Equal(Count, smoothed.Length);
        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(circle[i].X, smoothed[i].X, 1e-6);
            Assert.Equal(circle[i].Y, smoothed[i].Y, 1e-6);
        }
    }

    /// <summary>Smoothing a square with H = 8 keeps the area within five per cent (SPEC §8.1).</summary>
    [Fact]
    public void SmoothingASquareKeepsTheAreaWithinFivePercent()
    {
        Vec2[] square = TestShapes.Square(side: 4);
        Vec2[] smoothed = ContourSmoothing.FourierLowPass(square, harmonics: 8);

        double original = PolygonOps.SignedArea(square);
        double actual = PolygonOps.SignedArea(smoothed);
        double relative = Math.Abs(actual - original) / original;

        Assert.True(relative <= 0.05, $"The area changed by {relative:P2}, which is more than 5 %.");
    }

    /// <summary>The Fourier filter keeps the number of transform points (SPEC §4.4).</summary>
    [Fact]
    public void SmoothingReturnsTheTransformPointCount()
    {
        Vec2[] smoothed = ContourSmoothing.FourierLowPass(TestShapes.LShape(), harmonics: 12);
        Assert.Equal(ContourSmoothing.DefaultTransformPointCount, smoothed.Length);
    }

    /// <summary>Douglas-Peucker recovers the corners of a polygon that was densely sampled.</summary>
    [Fact]
    public void DouglasPeuckerRecoversPolygonCorners()
    {
        Vec2[] dense = PolygonOps.Resample(TestShapes.LShape(), 400);
        Vec2[] simplified = ContourSmoothing.DouglasPeucker(dense, 0.001);

        Assert.InRange(simplified.Length, 6, 12);
        Assert.Equal(PolygonOps.SignedArea(dense), PolygonOps.SignedArea(simplified), 0.01);
    }

    /// <summary>Consecutive duplicates are removed, including across the closing edge (SPEC §4.2).</summary>
    [Fact]
    public void DuplicatePointsAreRemoved()
    {
        Vec2[] withDuplicates =
        [
            new Vec2(0, 0),
            new Vec2(0, 0),
            new Vec2(1, 0),
            new Vec2(1, 1),
            new Vec2(1, 1),
            new Vec2(0, 0),
        ];

        Vec2[] cleaned = PolygonOps.RemoveConsecutiveDuplicates(withDuplicates, 1e-12);
        Assert.Equal(3, cleaned.Length);
    }

    /// <summary>The diameter of a circle of radius r is 2r.</summary>
    [Fact]
    public void DiameterOfACircleIsTwiceTheRadius()
    {
        Vec2[] circle = TestShapes.Circle(512, radius: 3);
        Assert.Equal(6.0, PolygonOps.Diameter(circle), 1e-3);
    }

    /// <summary>
    /// Finds the arc length from the first vertex to a point that lies on the boundary of
    /// the polygon, used to verify equal spacing.
    /// </summary>
    /// <param name="polygon">The source polygon.</param>
    /// <param name="point">A point on its boundary.</param>
    /// <returns>The arc length along the boundary.</returns>
    private static double ArcLengthOf(Vec2[] polygon, Vec2 point)
    {
        double cumulative = 0;
        double best = double.PositiveInfinity;
        double bestArcLength = 0;

        for (int i = 0; i < polygon.Length; i++)
        {
            Vec2 a = polygon[i];
            Vec2 b = polygon[(i + 1) % polygon.Length];
            double segmentLength = a.DistanceTo(b);
            if (segmentLength > 0)
            {
                double t = Math.Clamp((point - a).Dot(b - a) / (segmentLength * segmentLength), 0, 1);
                Vec2 projected = a + ((b - a) * t);
                double distance = point.DistanceTo(projected);
                if (distance < best)
                {
                    best = distance;
                    bestArcLength = cumulative + (t * segmentLength);
                }
            }

            cumulative += segmentLength;
        }

        Assert.True(best < 1e-9, $"The point is {best} away from the boundary.");
        return bestArcLength;
    }
}
