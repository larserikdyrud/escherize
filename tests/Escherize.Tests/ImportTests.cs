using System.Text.Json;
using Escherize.Geometry;
using Escherize.Imaging;
using Escherize.Io;
using Escherize.Preprocessing;
using Xunit;

namespace Escherize.Tests;

/// <summary>Image, polygon and GeoJSON import tests (SPEC §8.1).</summary>
public sealed class ImportTests
{
    /// <summary>
    /// A synthetic disc of radius 100 px yields a contour whose area is within one per
    /// cent of pi r squared (SPEC §8.1).
    /// </summary>
    [Fact]
    public void PngCircleHasTheExpectedArea()
    {
        const int Radius = 100;
        byte[] png = TestShapes.CirclePng(Radius);

        using var stream = new MemoryStream(png);
        PngContourExtractor.Result result = PngContourExtractor.Read(stream);

        double expected = Math.PI * Radius * Radius;
        double actual = PolygonOps.SignedArea(result.Contour);

        Assert.True(actual > 0, "The imported contour must be positively oriented.");
        double relative = Math.Abs(actual - expected) / expected;
        Assert.True(relative <= 0.01, $"The contour area differs by {relative:P2}, which is more than 1 %.");
    }

    /// <summary>The imported silhouette is round: every contour point is at radius r within one pixel.</summary>
    [Fact]
    public void PngCircleContourIsRound()
    {
        const int Radius = 60;
        byte[] png = TestShapes.CirclePng(Radius);

        using var stream = new MemoryStream(png);
        PngContourExtractor.Result result = PngContourExtractor.Read(stream);

        Vec2 centre = PolygonOps.PointAverage(result.Contour);
        foreach (Vec2 point in result.Contour)
        {
            Assert.Equal(Radius, point.DistanceTo(centre), 1.0);
        }
    }

    /// <summary>The Otsu threshold separates a two tone image between its two levels (SPEC §4.1).</summary>
    [Fact]
    public void OtsuSeparatesTwoLevels()
    {
        var luminance = new byte[200];
        var opaque = new bool[200];
        for (int i = 0; i < luminance.Length; i++)
        {
            luminance[i] = i < 100 ? (byte)30 : (byte)220;
            opaque[i] = true;
        }

        int threshold = PngContourExtractor.OtsuThreshold(luminance, opaque);
        Assert.InRange(threshold, 30, 219);
    }

    /// <summary>Enclosed background regions are filled before tracing (SPEC §4.1).</summary>
    [Fact]
    public void HolesAreFilled()
    {
        var mask = new BinaryMask(20, 20);
        for (int y = 5; y < 15; y++)
        {
            for (int x = 5; x < 15; x++)
            {
                mask[x, y] = true;
            }
        }

        mask[9, 9] = false;
        mask[10, 10] = false;

        BinaryMask cleaned = mask.KeepLargestComponentAndFillHoles();
        Assert.True(cleaned[9, 9]);
        Assert.True(cleaned[10, 10]);
        Assert.Equal(100, cleaned.ForegroundCount);
    }

    /// <summary>Only the largest eight-connected component survives (SPEC §4.1).</summary>
    [Fact]
    public void OnlyTheLargestComponentSurvives()
    {
        var mask = new BinaryMask(30, 30);
        for (int y = 2; y < 12; y++)
        {
            for (int x = 2; x < 12; x++)
            {
                mask[x, y] = true;
            }
        }

        mask[25, 25] = true;
        mask[26, 26] = true;

        BinaryMask cleaned = mask.KeepLargestComponent();
        Assert.Equal(100, cleaned.ForegroundCount);
        Assert.False(cleaned[25, 25]);
    }

    /// <summary>A polygon is read from the JSON form of SPEC §4.2.</summary>
    [Fact]
    public void PolygonJsonIsRead()
    {
        using JsonDocument document = JsonDocument.Parse("""{ "points": [[0,0],[2,0],[2,1],[0,1]] }""");
        Vec2[] polygon = PolygonReader.ReadJson(document.RootElement);

        Assert.Equal(4, polygon.Length);
        Assert.Equal(new Vec2(2, 1), polygon[2]);
        Assert.Equal(2.0, PolygonOps.SignedArea(polygon), 1e-12);
    }

    /// <summary>A polygon is read from CSV, with the optional header skipped (SPEC §4.2).</summary>
    [Fact]
    public void PolygonCsvIsRead()
    {
        string[] lines = ["x,y", "0,0", "2,0", "2,1", "0,1"];
        Vec2[] polygon = PolygonReader.ReadCsv(lines);

        Assert.Equal(4, polygon.Length);
        Assert.Equal(2.0, PolygonOps.SignedArea(polygon), 1e-12);
    }

    /// <summary>The projection centre maps to the origin (SPEC §8.1).</summary>
    [Fact]
    public void ProjectionCentreMapsToOrigin()
    {
        var projection = new LambertAzimuthalEqualArea(10.75, 59.91);
        Vec2 origin = projection.Project(10.75, 59.91);

        Assert.Equal(0.0, origin.X, 1e-9);
        Assert.Equal(0.0, origin.Y, 1e-9);
    }

    /// <summary>
    /// Two points placed symmetrically about the central meridian project to mirrored x
    /// and equal y (SPEC §8.1).
    /// </summary>
    [Fact]
    public void ProjectionIsMirrorSymmetricAboutTheCentralMeridian()
    {
        var projection = new LambertAzimuthalEqualArea(10.0, 60.0);

        Vec2 east = projection.Project(10.0 + 7.5, 63.0);
        Vec2 west = projection.Project(10.0 - 7.5, 63.0);

        Assert.Equal(-east.X, west.X, 1e-9);
        Assert.Equal(east.Y, west.Y, 1e-9);
        Assert.True(east.X > 0, "A point east of the central meridian must have positive x.");
    }

    /// <summary>The projection preserves area: a spherical cap maps to a disc of the same area.</summary>
    [Fact]
    public void ProjectionPreservesArea()
    {
        var projection = new LambertAzimuthalEqualArea(0, 90);

        // A polar cap reaching down to 80 degrees north.
        const double CapLatitude = 80.0;
        var ring = new Vec2[720];
        for (int i = 0; i < ring.Length; i++)
        {
            ring[i] = new Vec2(i * 360.0 / ring.Length, CapLatitude);
        }

        double projectedArea = Math.Abs(PolygonOps.SignedArea(projection.Project(ring)));

        double r = LambertAzimuthalEqualArea.EarthRadiusKm;
        double capArea = 2 * Math.PI * r * r * (1 - Math.Sin(double.DegreesToRadians(CapLatitude)));

        Assert.Equal(capArea, projectedArea, capArea * 1e-4);
    }

    /// <summary>A GeoJSON FeatureCollection is read and the largest ring is chosen (SPEC §4.3).</summary>
    [Fact]
    public void GeoJsonPicksTheLargestRing()
    {
        const string Json = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "properties": { "name": "Testland" },
              "geometry": {
                "type": "MultiPolygon",
                "coordinates": [
                  [[[10.0,60.0],[10.2,60.0],[10.2,60.1],[10.0,60.1],[10.0,60.0]]],
                  [[[11.0,60.0],[13.0,60.0],[13.0,61.0],[11.0,61.0],[11.0,60.0]]]
                ]
              }
            }
          ]
        }
        """;

        using JsonDocument document = JsonDocument.Parse(Json);
        GeoJsonReader.Result result = GeoJsonReader.Read(document.RootElement, "Testland");

        Assert.Equal(2, result.RingCount);
        Assert.Equal(1, result.RingIndex);
        Assert.Equal(4, result.Contour.Length);
    }

    /// <summary>An explicit ring index overrides the largest-area rule (SPEC §4.3).</summary>
    [Fact]
    public void GeoJsonHonoursAnExplicitRingIndex()
    {
        const string Json = """
        {
          "type": "MultiPolygon",
          "coordinates": [
            [[[10.0,60.0],[10.2,60.0],[10.2,60.1],[10.0,60.1],[10.0,60.0]]],
            [[[11.0,60.0],[13.0,60.0],[13.0,61.0],[11.0,61.0],[11.0,60.0]]]
          ]
        }
        """;

        using JsonDocument document = JsonDocument.Parse(Json);
        GeoJsonReader.Result result = GeoJsonReader.Read(document.RootElement, ringIndex: 0);

        Assert.Equal(0, result.RingIndex);
    }

    /// <summary>The normalised goal has zero mean, unit norm and positive area (SPEC §3, §4.5).</summary>
    [Fact]
    public void GoalShapeIsNormalised()
    {
        Vec2[] contour = TestShapes.Circle(300, radius: 17, center: new Vec2(-40, 90));
        GoalShape goal = Preprocessor.Build(contour, new PreprocessOptions { PointCount = 64 });

        Assert.Equal(64, goal.PointCount);

        double norm = 0;
        double sumX = 0;
        double sumY = 0;
        foreach (Vec2 point in goal.Points)
        {
            norm += point.LengthSquared;
            sumX += point.X;
            sumY += point.Y;
        }

        Assert.Equal(1.0, Math.Sqrt(norm), 1e-12);
        Assert.Equal(0.0, sumX, 1e-12);
        Assert.Equal(0.0, sumY, 1e-12);
        Assert.True(PolygonOps.SignedArea(goal.Points) > 0);
    }

    /// <summary>The coordinate vector uses the layout of SPEC §3 and the reversal of SPEC §4.5.</summary>
    [Fact]
    public void CoordinateVectorAndReversalFollowTheSpecification()
    {
        Vec2[] contour = TestShapes.LShape();
        GoalShape goal = Preprocessor.Build(contour, new PreprocessOptions { PointCount = 32, SmoothHarmonics = 0 });

        int n = goal.PointCount;
        for (int t = 0; t < n; t++)
        {
            Assert.Equal(goal.Points[t].X, goal.W[t]);
            Assert.Equal(goal.Points[t].Y, goal.W[n + t]);
            Assert.Equal(goal.Points[(n - t) % n], goal.ReversedPoints[t]);
        }

        Assert.True(PolygonOps.SignedArea(goal.ReversedPoints) < 0);
    }

    /// <summary>Denormalising undoes the normalisation, within rounding error (SPEC §3).</summary>
    [Fact]
    public void DenormalizeInvertsNormalisation()
    {
        Vec2[] contour = TestShapes.Circle(128, radius: 5, center: new Vec2(3, -2));
        GoalShape goal = Preprocessor.Build(contour, new PreprocessOptions { PointCount = 64, SmoothHarmonics = 0 });

        Vec2 centre = PolygonOps.PointAverage(goal.Points);
        Vec2 restored = goal.Denormalize(centre);

        Assert.Equal(goal.Centroid.X, restored.X, 1e-9);
        Assert.Equal(goal.Centroid.Y, restored.Y, 1e-9);
    }
}
