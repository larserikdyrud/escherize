using Escherize.Templates;

namespace Escherize.Parametrization;

/// <summary>
/// Builds the orthonormal basis B = [B''_d | B_s] explicitly (SPEC §5.3 to §5.5). This is
/// the dense builder; the fast evaluator of SPEC §6.3 uses the same description without
/// materialising B.
/// </summary>
public static class DenseBasisBuilder
{
    /// <summary>Builds the vertex parametrisation B_v of SPEC §5.3.</summary>
    /// <param name="template">The template.</param>
    /// <returns>A 2 nv by md matrix with orthonormal columns.</returns>
    /// <exception cref="InvalidOperationException">The template has fewer than three degrees of freedom.</exception>
    public static DenseMatrix BuildVertexBasis(TemplateSpec template)
    {
        ArgumentNullException.ThrowIfNull(template);

        DenseMatrix cornerConditions = RelationMatrix.CornerConditions(template);
        DenseMatrix basis = NullspaceSolver.Nullspace(cornerConditions);

        // Two translations and a scale are always free, so anything less means the corner
        // conditions were built wrongly (SPEC §5.3).
        if (basis.Columns < 3)
        {
            throw new InvalidOperationException(
                $"Template {template.Name} has only {basis.Columns} vertex degrees of freedom; at least 3 are required.");
        }

        return basis;
    }

    /// <summary>Builds the basis for a template and a k vector.</summary>
    /// <param name="template">The template.</param>
    /// <param name="k">The k vector.</param>
    /// <returns>The basis.</returns>
    public static TileBasis Build(TemplateSpec template, ReadOnlySpan<int> k) =>
        Build(TileLayout.Create(template, k));

    /// <summary>Builds the basis for a layout.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The basis.</returns>
    public static TileBasis Build(TileLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        DenseMatrix vertexBasis = BuildVertexBasis(layout.Template);
        int md = vertexBasis.Columns;
        int ms = layout.EdgeColumnCount;
        int n = layout.PointCount;
        int nv = layout.VertexCount;

        // Step 1: the vertex part B_d, expressing every point by its basis point.
        var vertexPart = new DenseMatrix(2 * n, md);

        for (int s = 0; s < nv; s++)
        {
            int index = layout.VertexIndex(s);
            vertexBasis.Row(s).CopyTo(vertexPart.Row(index));
            vertexBasis.Row(nv + s).CopyTo(vertexPart.Row(n + index));
        }

        // The middle point of an odd length C edge is pinned to the edge midpoint.
        for (int i = 0; i < layout.CMidpointIndices.Count; i++)
        {
            int index = layout.CMidpointIndices[i];
            int edge = layout.CMidpointEdges[i];
            WriteBasisPoint(vertexPart, vertexBasis, n, nv, index, BasisPoint.Midpoint(edge, (edge + 1) % nv));
        }

        foreach (EdgeRun run in layout.Runs)
        {
            for (int i = 1; i <= run.Length; i++)
            {
                WriteBasisPoint(vertexPart, vertexBasis, n, nv, run.P(i), run.PBasis);
                WriteBasisPoint(vertexPart, vertexBasis, n, nv, run.Q(i), run.QBasis);
            }
        }

        // Step 2: project the edge parameters out of the vertex part, so that the two
        // blocks of B are orthogonal. The block is the same for every index of a run, so
        // it is computed once and written back to all of them.
        foreach (EdgeRun run in layout.Runs)
        {
            ProjectRun(vertexPart, run, n, md);
        }

        // Step 3 and 4: whiten the vertex part with the Cholesky factor of its Gram matrix.
        DenseMatrix gram = vertexPart.GramMatrix();
        DenseMatrix cholesky = gram.Cholesky();
        DenseMatrix whitened = vertexPart.MultiplyByInverseTransposeOf(cholesky);

        // Assemble B = [B''_d | B_s].
        var b = new DenseMatrix(2 * n, md + ms);
        for (int r = 0; r < 2 * n; r++)
        {
            whitened.Row(r).CopyTo(b.Row(r)[..md]);
        }

        double inverseSqrt2 = 1.0 / Math.Sqrt(2.0);
        foreach (EdgeRun run in layout.Runs)
        {
            for (int i = 1; i <= run.Length; i++)
            {
                int p = run.P(i);
                int q = run.Q(i);
                int columnX = md + run.ColumnStart + (2 * (i - 1));
                int columnY = columnX + 1;

                b[p, columnX] = run.Lx[0] * inverseSqrt2;
                b[n + p, columnX] = run.Lx[1] * inverseSqrt2;
                b[q, columnX] = run.Lx[2] * inverseSqrt2;
                b[n + q, columnX] = run.Lx[3] * inverseSqrt2;

                b[p, columnY] = run.Ly[0] * inverseSqrt2;
                b[n + p, columnY] = run.Ly[1] * inverseSqrt2;
                b[q, columnY] = run.Ly[2] * inverseSqrt2;
                b[n + q, columnY] = run.Ly[3] * inverseSqrt2;
            }
        }

        return new TileBasis(layout, b, md);
    }

    /// <summary>
    /// Writes the rows of one point, expressing it by its basis point in terms of the
    /// vertex parameters (SPEC §5.5 step 1).
    /// </summary>
    /// <param name="target">The matrix being filled.</param>
    /// <param name="vertexBasis">The vertex parametrisation B_v.</param>
    /// <param name="n">The number of points.</param>
    /// <param name="nv">The number of vertices.</param>
    /// <param name="pointIndex">The point whose rows are written.</param>
    /// <param name="basis">The basis point.</param>
    private static void WriteBasisPoint(
        DenseMatrix target,
        DenseMatrix vertexBasis,
        int n,
        int nv,
        int pointIndex,
        BasisPoint basis)
    {
        Span<double> rowX = target.Row(pointIndex);
        Span<double> rowY = target.Row(n + pointIndex);
        rowX.Clear();
        rowY.Clear();

        if (basis.IsMidpoint)
        {
            for (int c = 0; c < vertexBasis.Columns; c++)
            {
                rowX[c] = 0.5 * (vertexBasis[basis.VertexA, c] + vertexBasis[basis.VertexB, c]);
                rowY[c] = 0.5 * (vertexBasis[nv + basis.VertexA, c] + vertexBasis[nv + basis.VertexB, c]);
            }
        }
        else
        {
            vertexBasis.Row(basis.VertexA).CopyTo(rowX);
            vertexBasis.Row(nv + basis.VertexA).CopyTo(rowY);
        }
    }

    /// <summary>
    /// Removes the component of the vertex part that lies in the span of one run's two
    /// edge columns, X' = (I - P) X with P = (L_x L_x^T + L_y L_y^T) / 2 (SPEC §5.5 step 2).
    /// </summary>
    /// <param name="vertexPart">The matrix being projected, modified in place.</param>
    /// <param name="run">The run.</param>
    /// <param name="n">The number of points.</param>
    /// <param name="md">The number of vertex columns.</param>
    private static void ProjectRun(DenseMatrix vertexPart, EdgeRun run, int n, int md)
    {
        // The four rows of the block, in the order (x_p, y_p, x_q, y_q).
        int p = run.P(1);
        int q = run.Q(1);
        Span<int> blockRows = [p, n + p, q, n + q];

        var block = new double[4 * md];
        for (int i = 0; i < 4; i++)
        {
            vertexPart.Row(blockRows[i]).CopyTo(block.AsSpan(i * md, md));
        }

        var projected = new double[4 * md];
        block.CopyTo(projected, 0);

        // L_x and L_y are orthogonal, so removing them one after the other is the same as
        // applying the combined projector.
        foreach (double[] form in (double[][])[run.Lx, run.Ly])
        {
            // The forms have squared norm two, so the projector uses a factor of one half.
            for (int c = 0; c < md; c++)
            {
                double dot = 0;
                for (int i = 0; i < 4; i++)
                {
                    dot += form[i] * projected[(i * md) + c];
                }

                dot *= 0.5;
                for (int i = 0; i < 4; i++)
                {
                    projected[(i * md) + c] -= form[i] * dot;
                }
            }
        }

        // The block does not depend on the index within the run, so every index gets it.
        for (int i = 1; i <= run.Length; i++)
        {
            Span<int> rows = [run.P(i), n + run.P(i), run.Q(i), n + run.Q(i)];
            for (int j = 0; j < 4; j++)
            {
                projected.AsSpan(j * md, md).CopyTo(vertexPart.Row(rows[j]));
            }
        }
    }
}
