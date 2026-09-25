using System.Globalization;

namespace Escherize.Geometry;

/// <summary>
/// A point or vector in the plane. Coordinates follow the y-up convention (SPEC §3);
/// image coordinates are mirrored on import.
/// </summary>
/// <param name="X">The x coordinate.</param>
/// <param name="Y">The y coordinate.</param>
public readonly record struct Vec2(double X, double Y)
{
    /// <summary>The origin.</summary>
    public static Vec2 Zero => new(0, 0);

    /// <summary>Adds two vectors.</summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The sum.</returns>
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Subtracts one vector from another.</summary>
    /// <param name="a">The vector to subtract from.</param>
    /// <param name="b">The vector to subtract.</param>
    /// <returns>The difference.</returns>
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Negates a vector.</summary>
    /// <param name="a">The vector.</param>
    /// <returns>The negated vector.</returns>
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);

    /// <summary>Scales a vector.</summary>
    /// <param name="a">The vector.</param>
    /// <param name="s">The scale factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);

    /// <summary>Scales a vector.</summary>
    /// <param name="s">The scale factor.</param>
    /// <param name="a">The vector.</param>
    /// <returns>The scaled vector.</returns>
    public static Vec2 operator *(double s, Vec2 a) => a * s;

    /// <summary>Divides a vector by a scalar.</summary>
    /// <param name="a">The vector.</param>
    /// <param name="s">The divisor.</param>
    /// <returns>The divided vector.</returns>
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    /// <summary>The squared euclidean length.</summary>
    public double LengthSquared => (X * X) + (Y * Y);

    /// <summary>The euclidean length.</summary>
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>The dot product with another vector.</summary>
    /// <param name="other">The other vector.</param>
    /// <returns>The dot product.</returns>
    public double Dot(Vec2 other) => (X * other.X) + (Y * other.Y);

    /// <summary>The two dimensional cross product with another vector.</summary>
    /// <param name="other">The other vector.</param>
    /// <returns>The z component of the cross product.</returns>
    public double Cross(Vec2 other) => (X * other.Y) - (Y * other.X);

    /// <summary>The euclidean distance to another point.</summary>
    /// <param name="other">The other point.</param>
    /// <returns>The distance.</returns>
    public double DistanceTo(Vec2 other) => (this - other).Length;

    /// <summary>Linearly interpolates between two points.</summary>
    /// <param name="a">The point at <paramref name="t"/> = 0.</param>
    /// <param name="b">The point at <paramref name="t"/> = 1.</param>
    /// <param name="t">The interpolation parameter.</param>
    /// <returns>The interpolated point.</returns>
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

    /// <summary>Rotates the vector about the origin.</summary>
    /// <param name="radians">The rotation angle in radians, counter-clockwise.</param>
    /// <returns>The rotated vector.</returns>
    public Vec2 Rotate(double radians)
    {
        double c = Math.Cos(radians);
        double s = Math.Sin(radians);
        return new Vec2((c * X) - (s * Y), (s * X) + (c * Y));
    }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R})");
}
