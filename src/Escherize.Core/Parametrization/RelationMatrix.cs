using Escherize.Geometry;
using Escherize.Templates;

namespace Escherize.Parametrization;

/// <summary>
/// Builds the linear constraint matrices of SPEC §5.1: the corner conditions on the
/// tiling vertices alone, and the full system over all 2n point coordinates.
/// </summary>
public static class RelationMatrix
{
    /// <summary>
    /// The corner conditions, as a matrix over the 2 nv vertex coordinates with the x
    /// block first (SPEC §5.3). Every pair contributes two rows; C edges contribute none.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <returns>The constraint matrix A_v.</returns>
    public static DenseMatrix CornerConditions(TemplateSpec template)
    {
        ArgumentNullException.ThrowIfNull(template);

        int nv = template.VertexCount;
        (int A, int B)[] pairs = template.Pairs();
        var matrix = new DenseMatrix(2 * pairs.Length, 2 * nv);

        int row = 0;
        foreach ((int a, int b) in pairs)
        {
            EdgeSpec edge = template.Edges[a];
            int a0 = a % nv;
            int a1 = (a + 1) % nv;
            int b0 = b % nv;
            int b1 = (b + 1) % nv;

            switch (edge.Kind)
            {
                case EdgeKind.T:
                    // V_(b+1) - V_b = V_a - V_(a+1)
                    AddVertex(matrix, row, nv, b1, 1, 1);
                    AddVertex(matrix, row, nv, b0, -1, -1);
                    AddVertex(matrix, row, nv, a0, -1, -1);
                    AddVertex(matrix, row, nv, a1, 1, 1);
                    break;

                case EdgeKind.G:
                {
                    // V_(b+1) - V_b = F (V_(a+1) - V_a)
                    (double fx, double fy) = edge.Axis.MirrorFactors();
                    AddVertex(matrix, row, nv, b1, 1, 1);
                    AddVertex(matrix, row, nv, b0, -1, -1);
                    AddVertex(matrix, row, nv, a1, -fx, -fy);
                    AddVertex(matrix, row, nv, a0, fx, fy);
                    break;
                }

                case EdgeKind.R:
                {
                    // V_(b+1) - V = R(theta) (V_a - V), with V = V_b
                    double cos = Math.Cos(edge.ThetaRadians);
                    double sin = Math.Sin(edge.ThetaRadians);

                    matrix[row, b1] += 1;
                    matrix[row + 1, nv + b1] += 1;
                    matrix[row, b0] -= 1;
                    matrix[row + 1, nv + b0] -= 1;

                    // -R(theta) (V_a - V_b) mixes the x and y blocks.
                    matrix[row, a0] -= cos;
                    matrix[row, nv + a0] += sin;
                    matrix[row, b0] += cos;
                    matrix[row, nv + b0] -= sin;

                    matrix[row + 1, a0] -= sin;
                    matrix[row + 1, nv + a0] -= cos;
                    matrix[row + 1, b0] += sin;
                    matrix[row + 1, nv + b0] += cos;
                    break;
                }

                default:
                    throw new InvalidOperationException($"Edge kind {edge.Kind} cannot be a pair.");
            }

            row += 2;
        }

        return matrix;
    }

    /// <summary>
    /// The full relation system over the 2n point coordinates, covering the interior
    /// points as well as the corner conditions (SPEC §5.1, §8.3).
    /// </summary>
    /// <param name="layout">The layout, which fixes n and the point indices.</param>
    /// <returns>The constraint matrix A.</returns>
    public static DenseMatrix AllRelations(TileLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        TemplateSpec template = layout.Template;
        int nv = layout.VertexCount;
        int n = layout.PointCount;
        var rows = new List<double[]>();

        for (int s = 0; s < nv; s++)
        {
            if (template.Edges[s].Kind != EdgeKind.C)
            {
                continue;
            }

            int k = layout.EdgeK(s);
            int vs = layout.EdgePointIndex(s, 0);
            int vs1 = layout.EdgePointIndex(s, k + 1);

            // e(i) + e(k+1-i) - V_s - V_(s+1) = 0
            for (int i = 1; i <= k / 2; i++)
            {
                int p = layout.EdgePointIndex(s, i);
                int q = layout.EdgePointIndex(s, k + 1 - i);
                foreach (int block in Blocks(n))
                {
                    var row = new double[2 * n];
                    row[block + p] += 1;
                    row[block + q] += 1;
                    row[block + vs] -= 1;
                    row[block + vs1] -= 1;
                    rows.Add(row);
                }
            }

            // For odd k the middle point is pinned to the midpoint of the edge.
            if (k % 2 == 1)
            {
                int middle = layout.EdgePointIndex(s, (k + 1) / 2);
                foreach (int block in Blocks(n))
                {
                    var row = new double[2 * n];
                    row[block + middle] += 1;
                    row[block + vs] -= 0.5;
                    row[block + vs1] -= 0.5;
                    rows.Add(row);
                }
            }
        }

        foreach ((int a, int b) in template.Pairs())
        {
            EdgeSpec edge = template.Edges[a];
            int k = layout.EdgeK(a);

            for (int i = 1; i <= k + 1; i++)
            {
                switch (edge.Kind)
                {
                    case EdgeKind.T:
                    {
                        // b(i) - b(0) = a(k+1-i) - a(k+1)
                        int bi = layout.EdgePointIndex(b, i);
                        int b0 = layout.EdgePointIndex(b, 0);
                        int ai = layout.EdgePointIndex(a, k + 1 - i);
                        int a1 = layout.EdgePointIndex(a, k + 1);
                        foreach (int block in Blocks(n))
                        {
                            var row = new double[2 * n];
                            row[block + bi] += 1;
                            row[block + b0] -= 1;
                            row[block + ai] -= 1;
                            row[block + a1] += 1;
                            rows.Add(row);
                        }

                        break;
                    }

                    case EdgeKind.G:
                    {
                        // b(i) - b(0) = F (a(i) - a(0))
                        (double fx, double fy) = edge.Axis.MirrorFactors();
                        int bi = layout.EdgePointIndex(b, i);
                        int b0 = layout.EdgePointIndex(b, 0);
                        int ai = layout.EdgePointIndex(a, i);
                        int a0 = layout.EdgePointIndex(a, 0);

                        var rowX = new double[2 * n];
                        rowX[bi] += 1;
                        rowX[b0] -= 1;
                        rowX[ai] -= fx;
                        rowX[a0] += fx;
                        rows.Add(rowX);

                        var rowY = new double[2 * n];
                        rowY[n + bi] += 1;
                        rowY[n + b0] -= 1;
                        rowY[n + ai] -= fy;
                        rowY[n + a0] += fy;
                        rows.Add(rowY);
                        break;
                    }

                    case EdgeKind.R:
                    {
                        // b(i) - V = R(theta) (a(k+1-i) - V), with V = b(0)
                        double cos = Math.Cos(edge.ThetaRadians);
                        double sin = Math.Sin(edge.ThetaRadians);
                        int bi = layout.EdgePointIndex(b, i);
                        int v = layout.EdgePointIndex(b, 0);
                        int ai = layout.EdgePointIndex(a, k + 1 - i);

                        var rowX = new double[2 * n];
                        rowX[bi] += 1;
                        rowX[v] -= 1;
                        rowX[ai] -= cos;
                        rowX[v] += cos;
                        rowX[n + ai] += sin;
                        rowX[n + v] -= sin;
                        rows.Add(rowX);

                        var rowY = new double[2 * n];
                        rowY[n + bi] += 1;
                        rowY[n + v] -= 1;
                        rowY[ai] -= sin;
                        rowY[v] += sin;
                        rowY[n + ai] -= cos;
                        rowY[n + v] += cos;
                        rows.Add(rowY);
                        break;
                    }

                    default:
                        throw new InvalidOperationException($"Edge kind {edge.Kind} cannot be a pair.");
                }
            }
        }

        var matrix = new DenseMatrix(rows.Count, 2 * n);
        for (int r = 0; r < rows.Count; r++)
        {
            rows[r].CopyTo(matrix.Row(r));
        }

        return matrix;
    }

    /// <summary>The offsets of the x and y blocks of a coordinate vector (SPEC §3).</summary>
    /// <param name="n">The number of points.</param>
    /// <returns>Zero and n.</returns>
    private static int[] Blocks(int n) => [0, n];

    /// <summary>
    /// Adds a coefficient to the x row and the y row of one vertex, in the paired layout
    /// where row is the x equation and row + 1 the y equation.
    /// </summary>
    /// <param name="matrix">The matrix.</param>
    /// <param name="row">The index of the x equation.</param>
    /// <param name="nv">The number of vertices.</param>
    /// <param name="vertex">The vertex.</param>
    /// <param name="xFactor">The coefficient in the x equation.</param>
    /// <param name="yFactor">The coefficient in the y equation.</param>
    private static void AddVertex(DenseMatrix matrix, int row, int nv, int vertex, double xFactor, double yFactor)
    {
        matrix[row, vertex] += xFactor;
        matrix[row + 1, nv + vertex] += yFactor;
    }
}
