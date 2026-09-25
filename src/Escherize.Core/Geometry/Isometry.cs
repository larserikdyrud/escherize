using System.Globalization;

namespace Escherize.Geometry;

/// <summary>
/// An affine map of the plane, stored as the 2x3 matrix of SPEC §9.2: the point (x, y)
/// maps to (A x + B y + Tx, C x + D y + Ty).
/// </summary>
/// <param name="A">Row one, column one.</param>
/// <param name="B">Row one, column two.</param>
/// <param name="C">Row two, column one.</param>
/// <param name="D">Row two, column two.</param>
/// <param name="Tx">The horizontal translation.</param>
/// <param name="Ty">The vertical translation.</param>
public readonly record struct Isometry(double A, double B, double C, double D, double Tx, double Ty)
{
    /// <summary>The identity map.</summary>
    public static Isometry Identity => new(1, 0, 0, 1, 0, 0);

    /// <summary>A pure translation.</summary>
    /// <param name="offset">The translation vector.</param>
    /// <returns>The map.</returns>
    public static Isometry Translation(Vec2 offset) => new(1, 0, 0, 1, offset.X, offset.Y);

    /// <summary>A rotation about a centre.</summary>
    /// <param name="center">The centre of rotation.</param>
    /// <param name="radians">The angle, counter-clockwise.</param>
    /// <returns>The map.</returns>
    public static Isometry Rotation(Vec2 center, double radians)
    {
        double c = Math.Cos(radians);
        double s = Math.Sin(radians);
        return new Isometry(
            c, -s,
            s, c,
            center.X - (c * center.X) + (s * center.Y),
            center.Y - (s * center.X) - (c * center.Y));
    }

    /// <summary>
    /// A half turn about a point, which is the neighbour isometry of a C edge (SPEC §5.1).
    /// </summary>
    /// <param name="center">The centre of the half turn.</param>
    /// <returns>The map.</returns>
    public static Isometry HalfTurn(Vec2 center) =>
        new(-1, 0, 0, -1, 2 * center.X, 2 * center.Y);

    /// <summary>
    /// A reflection in one of the axes through a point, followed by no translation. The
    /// matrix is diag(1, -1) for the x axis and diag(-1, 1) for the y axis (SPEC §5.1).
    /// </summary>
    /// <param name="factors">The diagonal entries of the mirror matrix F.</param>
    /// <param name="source">The point that is mapped to <paramref name="target"/>.</param>
    /// <param name="target">The image of <paramref name="source"/>.</param>
    /// <returns>The map x to target + F (x - source).</returns>
    public static Isometry Mirror((double X, double Y) factors, Vec2 source, Vec2 target) => new(
        factors.X, 0, 0, factors.Y,
        target.X - (factors.X * source.X),
        target.Y - (factors.Y * source.Y));

    /// <summary>Applies the map to a point.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The image.</returns>
    public Vec2 Apply(Vec2 point) => new(
        (A * point.X) + (B * point.Y) + Tx,
        (C * point.X) + (D * point.Y) + Ty);

    /// <summary>Composes two maps: the result applies <paramref name="second"/> after this one.</summary>
    /// <param name="second">The map applied second.</param>
    /// <returns>The composition.</returns>
    public Isometry Then(Isometry second) => new(
        (second.A * A) + (second.B * C),
        (second.A * B) + (second.B * D),
        (second.C * A) + (second.D * C),
        (second.C * B) + (second.D * D),
        (second.A * Tx) + (second.B * Ty) + second.Tx,
        (second.C * Tx) + (second.D * Ty) + second.Ty);

    /// <summary>The determinant of the linear part; -1 for an orientation reversing map.</summary>
    public double Determinant => (A * D) - (B * C);

    /// <summary>The inverse map.</summary>
    /// <returns>The inverse.</returns>
    /// <exception cref="InvalidOperationException">The map is singular.</exception>
    public Isometry Inverse()
    {
        double determinant = Determinant;
        if (Math.Abs(determinant) < 1e-12)
        {
            throw new InvalidOperationException("A singular map cannot be inverted.");
        }

        double ia = D / determinant;
        double ib = -B / determinant;
        double ic = -C / determinant;
        double id = A / determinant;
        return new Isometry(ia, ib, ic, id, -((ia * Tx) + (ib * Ty)), -((ic * Tx) + (id * Ty)));
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"[{A:0.####} {B:0.####} {Tx:0.####}; {C:0.####} {D:0.####} {Ty:0.####}]");
}
