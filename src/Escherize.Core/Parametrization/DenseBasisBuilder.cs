using Escherize.Templates;

namespace Escherize.Parametrization;

/// <summary>
/// Materialises the orthonormal basis B = [B''_d | B_s] from a plan (SPEC §5.3 to §5.5).
/// This is the dense builder; the fast evaluator of SPEC §6.3 works from the same plan
/// without ever forming B.
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
        return Build(BasisPlan.Create(layout));
    }

    /// <summary>Materialises the basis described by a plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The basis.</returns>
    public static TileBasis Build(BasisPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        TileLayout layout = plan.Layout;
        TemplatePlan template = plan.Template;
        int md = plan.Md;
        int n = layout.PointCount;
        int nv = layout.VertexCount;

        // Steps 1 and 2 of SPEC §5.5: every row of B'_d is either a tiling vertex row, a
        // pinned midpoint row, or one of the four projected rows of a run.
        var vertexPart = new DenseMatrix(2 * n, md);

        for (int s = 0; s < nv; s++)
        {
            int index = plan.VertexPoints[s];
            template.Vertices[s].RowX.CopyTo(vertexPart.Row(index));
            template.Vertices[s].RowY.CopyTo(vertexPart.Row(n + index));
        }

        for (int i = 0; i < plan.MidpointEdges.Length; i++)
        {
            PlanAnchor anchor = template.EdgeMidpoints[plan.MidpointEdges[i]]!;
            int index = plan.MidpointPoints[i];
            anchor.RowX.CopyTo(vertexPart.Row(index));
            anchor.RowY.CopyTo(vertexPart.Row(n + index));
        }

        foreach (RunPlacement placement in plan.Placements)
        {
            PlanRun run = template.Runs[placement.Slot];
            for (int i = 1; i <= placement.Length; i++)
            {
                int p = BasisPlan.PPoint(placement, i);
                int q = plan.QPoint(placement, placement.Slot, i);
                run.Block.AsSpan(0 * md, md).CopyTo(vertexPart.Row(p));
                run.Block.AsSpan(1 * md, md).CopyTo(vertexPart.Row(n + p));
                run.Block.AsSpan(2 * md, md).CopyTo(vertexPart.Row(q));
                run.Block.AsSpan(3 * md, md).CopyTo(vertexPart.Row(n + q));
            }
        }

        // Steps 3 and 4: whiten with the Cholesky factor of the Gram matrix.
        DenseMatrix whitened = vertexPart.MultiplyByInverseTransposeOf(plan.Cholesky);

        var b = new DenseMatrix(2 * n, plan.ColumnCount);
        for (int r = 0; r < 2 * n; r++)
        {
            whitened.Row(r).CopyTo(b.Row(r)[..md]);
        }

        int column = md;
        foreach (RunPlacement placement in plan.Placements)
        {
            PlanRun run = template.Runs[placement.Slot];
            for (int i = 1; i <= placement.Length; i++)
            {
                int p = BasisPlan.PPoint(placement, i);
                int q = plan.QPoint(placement, placement.Slot, i);

                b[p, column] = run.Lx[0];
                b[n + p, column] = run.Lx[1];
                b[q, column] = run.Lx[2];
                b[n + q, column] = run.Lx[3];

                b[p, column + 1] = run.Ly[0];
                b[n + p, column + 1] = run.Ly[1];
                b[q, column + 1] = run.Ly[2];
                b[n + q, column + 1] = run.Ly[3];
                column += 2;
            }
        }

        return new TileBasis(layout, b, md);
    }
}
