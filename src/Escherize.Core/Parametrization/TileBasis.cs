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

    /// <summary>
    /// Projects the goal shifted by j onto the basis, computing both B transposed times
    /// w_j and B transposed times the goal rotated by minus ninety degrees, in one pass
    /// over the matrix (SPEC §6.1, §6.2).
    /// </summary>
    /// <param name="doubledX">The goal x coordinates, repeated twice, of length 2n.</param>
    /// <param name="doubledY">The goal y coordinates, repeated twice, of length 2n.</param>
    /// <param name="shift">The start offset j.</param>
    /// <param name="projection">Receives B transposed times w_j.</param>
    /// <param name="rotatedProjection">
    /// Receives B transposed times the rotated goal, or an empty span when the template
    /// does not need it.
    /// </param>
    public void ProjectShifted(
        ReadOnlySpan<double> doubledX,
        ReadOnlySpan<double> doubledY,
        int shift,
        Span<double> projection,
        Span<double> rotatedProjection)
    {
        int n = Layout.PointCount;
        int m = ColumnCount;
        projection.Clear();
        bool rotated = rotatedProjection.Length == m;
        if (rotated)
        {
            rotatedProjection.Clear();
        }

        ReadOnlySpan<double> values = _b.Values;
        for (int t = 0; t < n; t++)
        {
            double x = doubledX[t + shift];
            double y = doubledY[t + shift];
            int rowX = t * m;
            int rowY = (n + t) * m;

            for (int c = 0; c < m; c++)
            {
                double bx = values[rowX + c];
                double by = values[rowY + c];
                projection[c] += (bx * x) + (by * y);
                if (rotated)
                {
                    // The rotated goal is (y, -x), so the same row values apply swapped.
                    rotatedProjection[c] += (bx * y) - (by * x);
                }
            }
        }
    }

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
