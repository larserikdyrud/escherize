using Escherize.Geometry;

namespace Escherize.Io;

/// <summary>
/// Spherical Lambert azimuthal equal-area projection (SPEC §4.3). Areas are preserved,
/// which matters because the search compares shapes, not distances on the sphere.
/// </summary>
public sealed class LambertAzimuthalEqualArea
{
    /// <summary>The earth radius used by the projection, in kilometres (SPEC §4.3).</summary>
    public const double EarthRadiusKm = 6371.0;

    private readonly double _sinPhi1;
    private readonly double _cosPhi1;

    /// <summary>Creates a projection centred on the given point.</summary>
    /// <param name="centerLongitudeDegrees">The central meridian, lambda zero, in degrees.</param>
    /// <param name="centerLatitudeDegrees">The central parallel, phi one, in degrees.</param>
    public LambertAzimuthalEqualArea(double centerLongitudeDegrees, double centerLatitudeDegrees)
    {
        CenterLongitudeDegrees = centerLongitudeDegrees;
        CenterLatitudeDegrees = centerLatitudeDegrees;

        double phi1 = double.DegreesToRadians(centerLatitudeDegrees);
        _sinPhi1 = Math.Sin(phi1);
        _cosPhi1 = Math.Cos(phi1);
    }

    /// <summary>The central meridian in degrees.</summary>
    public double CenterLongitudeDegrees { get; }

    /// <summary>The central parallel in degrees.</summary>
    public double CenterLatitudeDegrees { get; }

    /// <summary>Projects a geographic coordinate to the plane, in kilometres.</summary>
    /// <param name="longitudeDegrees">The longitude in degrees.</param>
    /// <param name="latitudeDegrees">The latitude in degrees.</param>
    /// <returns>The projected point, y-up, in kilometres.</returns>
    public Vec2 Project(double longitudeDegrees, double latitudeDegrees)
    {
        double phi = double.DegreesToRadians(latitudeDegrees);
        double deltaLambda = double.DegreesToRadians(longitudeDegrees - CenterLongitudeDegrees);

        double sinPhi = Math.Sin(phi);
        double cosPhi = Math.Cos(phi);
        double cosDelta = Math.Cos(deltaLambda);

        double denominator = 1 + (_sinPhi1 * sinPhi) + (_cosPhi1 * cosPhi * cosDelta);

        // The antipode of the projection centre has no image; it is clamped rather than
        // producing an infinity, since a country outline never spans half the globe.
        denominator = Math.Max(denominator, 1e-12);
        double kPrime = Math.Sqrt(2.0 / denominator);

        double x = EarthRadiusKm * kPrime * cosPhi * Math.Sin(deltaLambda);
        double y = EarthRadiusKm * kPrime * ((_cosPhi1 * sinPhi) - (_sinPhi1 * cosPhi * cosDelta));
        return new Vec2(x, y);
    }

    /// <summary>Projects a whole ring of geographic coordinates.</summary>
    /// <param name="ring">The ring as longitude and latitude pairs in degrees.</param>
    /// <returns>The projected ring.</returns>
    public Vec2[] Project(ReadOnlySpan<Vec2> ring)
    {
        var result = new Vec2[ring.Length];
        for (int i = 0; i < ring.Length; i++)
        {
            result[i] = Project(ring[i].X, ring[i].Y);
        }

        return result;
    }
}
