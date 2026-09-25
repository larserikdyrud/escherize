using Escherize.Geometry;
using Escherize.Templates;

namespace Escherize.Parametrization;

/// <summary>
/// The orthonormal basis B of the tile space for one template and one k vector
/// (SPEC §5.5). A tile is u = B xi, and every column is a degree of freedom that keeps
/// the tile compatible with the template.
/// </summary>
public sealed class TileBasis
{
    private readonly DenseMatrix _b;

    internal TileBasis(TileLayout layout, DenseMatrix b, int vertexColumnCount)
    {
        Layout = layout;
        _b = b;
        VertexColumnCount = vertexColumnCount;
    }

    /// <summary>The layout the basis was built for.</summary>
    public TileLayout Layout { get; }

    /// <summary>The template the basis was built for.</summary>
    public TemplateSpec Template => Layout.Template;

    /// <summary>The number of rows, 2n.</summary>
    public int RowCount => _b.Rows;

    /// <summary>The number of columns, m = md + ms.</summary>
    public int ColumnCount => _b.Columns;

    /// <summary>
    /// The number of columns that come from the tiling vertices, md (SPEC §5.3). The
    /// remaining columns come from the edge parameters.
    /// </summary>
    public int VertexColumnCount { get; }

    /// <summary>The number of columns that come from the edge parameters, ms (SPEC §5.4).</summary>
    public int EdgeColumnCount => ColumnCount - VertexColumnCount;

    /// <summary>The matrix itself.</summary>
    public DenseMatrix Matrix => _b;

    /// <summary>Computes B xi, the coordinate vector of a tile.</summary>
    /// <param name="xi">The parameter vector, of length <see cref="ColumnCount"/>.</param>
    /// <returns>The coordinate vector, of length 2n.</returns>
    public double[] ToCoordinates(ReadOnlySpan<double> xi) => _b.Multiply(xi);

    /// <summary>Computes B transposed times w.</summary>
    /// <param name="w">The coordinate vector, of length 2n.</param>
    /// <returns>The parameter vector, of length <see cref="ColumnCount"/>.</returns>
    public double[] Project(ReadOnlySpan<double> w) => _b.TransposeMultiply(w);

    /// <summary>Converts a coordinate vector into points (SPEC §3).</summary>
    /// <param name="coordinates">The coordinate vector of length 2n.</param>
    /// <returns>The n points.</returns>
    public Vec2[] ToPoints(ReadOnlySpan<double> coordinates)
    {
        int n = Layout.PointCount;
        if (coordinates.Length != 2 * n)
        {
            throw new ArgumentException($"Expected {2 * n} coordinates, got {coordinates.Length}.", nameof(coordinates));
        }

        var points = new Vec2[n];
        for (int t = 0; t < n; t++)
        {
            points[t] = new Vec2(coordinates[t], coordinates[n + t]);
        }

        return points;
    }

    /// <summary>Builds the tile points for a parameter vector.</summary>
    /// <param name="xi">The parameter vector.</param>
    /// <returns>The n tile points, in boundary order.</returns>
    public Vec2[] ToTile(ReadOnlySpan<double> xi) => ToPoints(ToCoordinates(xi));
}
