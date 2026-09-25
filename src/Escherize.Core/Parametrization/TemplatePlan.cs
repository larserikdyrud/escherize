using System.Collections.Concurrent;
using Escherize.Templates;

namespace Escherize.Parametrization;

/// <summary>
/// The part of a run description that depends only on the template, not on the k vector
/// (SPEC §5.4, §5.5). The placement of the run along the boundary is added per k vector by
/// <see cref="BasisPlan"/>.
/// </summary>
/// <param name="Slot">The index of the run within the template.</param>
/// <param name="Kind">The relation kind.</param>
/// <param name="EdgeA">The first edge, or the edge itself for a C run.</param>
/// <param name="EdgeB">The second edge, or the edge itself for a C run.</param>
/// <param name="Diagonal">Whether P and Q run in the same direction (SPEC §5.4).</param>
/// <param name="SwapSides">
/// Whether the two sides were exchanged so that P is the ascending index, which the fast
/// evaluator needs (SPEC §6.3 step 1).
/// </param>
/// <param name="Lx">The x column form over (x_p, y_p, x_q, y_q), already scaled by 1/sqrt(2).</param>
/// <param name="Ly">The y column form, already scaled.</param>
/// <param name="JLx">J transposed applied to <paramref name="Lx"/>, for the Procrustes terms.</param>
/// <param name="JLy">J transposed applied to <paramref name="Ly"/>.</param>
/// <param name="Block">The projected basis block X', four rows of md values, row major.</param>
/// <param name="BlockGram">X' transposed times X', md by md, row major.</param>
public sealed record PlanRun(
    int Slot,
    EdgeKind Kind,
    int EdgeA,
    int EdgeB,
    bool Diagonal,
    bool SwapSides,
    double[] Lx,
    double[] Ly,
    double[] JLx,
    double[] JLy,
    double[] Block,
    double[] BlockGram);

/// <summary>
/// The rows of one point that is pinned to a fixed combination of tiling vertices: a
/// tiling vertex itself, or the middle point of an odd length C edge (SPEC §5.5).
/// </summary>
/// <param name="RowX">The row of the x coordinate, md values.</param>
/// <param name="RowY">The row of the y coordinate, md values.</param>
/// <param name="Gram">The contribution of the two rows to the Gram matrix, md by md.</param>
public sealed record PlanAnchor(double[] RowX, double[] RowY, double[] Gram);

/// <summary>
/// Everything about a template that does not depend on the k vector (SPEC §5.3 to §5.5).
/// Building it involves a null space computation, so the results are cached.
/// </summary>
public sealed class TemplatePlan
{
    private static readonly ConcurrentDictionary<TemplateSpec, TemplatePlan> Cache = new();

    private TemplatePlan(
        TemplateSpec template,
        DenseMatrix vertexBasis,
        PlanAnchor[] vertices,
        PlanAnchor?[] edgeMidpoints,
        PlanRun[] runs,
        double[] vertexGram)
    {
        Template = template;
        VertexBasis = vertexBasis;
        Vertices = vertices;
        EdgeMidpoints = edgeMidpoints;
        Runs = runs;
        VertexGram = vertexGram;
    }

    /// <summary>The template.</summary>
    public TemplateSpec Template { get; }

    /// <summary>The vertex parametrisation B_v (SPEC §5.3).</summary>
    public DenseMatrix VertexBasis { get; }

    /// <summary>The number of vertex degrees of freedom, md.</summary>
    public int Md => VertexBasis.Columns;

    /// <summary>The rows of each tiling vertex, indexed by vertex.</summary>
    public PlanAnchor[] Vertices { get; }

    /// <summary>
    /// The rows of the midpoint of each edge, indexed by edge. Only C edges have an entry;
    /// it is used when the edge has an odd number of interior points.
    /// </summary>
    public PlanAnchor?[] EdgeMidpoints { get; }

    /// <summary>The runs, one per C edge and one per pair (SPEC §5.4).</summary>
    public PlanRun[] Runs { get; }

    /// <summary>The contribution of the tiling vertices to the Gram matrix, md by md.</summary>
    public double[] VertexGram { get; }

    /// <summary>Returns the plan for a template, building it on first use.</summary>
    /// <param name="template">The template.</param>
    /// <returns>The plan.</returns>
    public static TemplatePlan For(TemplateSpec template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return Cache.GetOrAdd(template, Build);
    }

    /// <summary>Builds the plan.</summary>
    /// <param name="template">The template.</param>
    /// <returns>The plan.</returns>
    private static TemplatePlan Build(TemplateSpec template)
    {
        DenseMatrix vertexBasis = DenseBasisBuilder.BuildVertexBasis(template);
        int md = vertexBasis.Columns;
        int nv = template.VertexCount;

        var vertices = new PlanAnchor[nv];
        var vertexGram = new double[md * md];
        for (int s = 0; s < nv; s++)
        {
            double[] rowX = vertexBasis.Row(s).ToArray();
            double[] rowY = vertexBasis.Row(nv + s).ToArray();
            double[] gram = OuterSum(rowX, rowY, md);
            vertices[s] = new PlanAnchor(rowX, rowY, gram);
            for (int i = 0; i < gram.Length; i++)
            {
                vertexGram[i] += gram[i];
            }
        }

        var midpoints = new PlanAnchor?[nv];
        for (int s = 0; s < nv; s++)
        {
            if (template.Edges[s].Kind != EdgeKind.C)
            {
                continue;
            }

            int next = (s + 1) % nv;
            var rowX = new double[md];
            var rowY = new double[md];
            for (int c = 0; c < md; c++)
            {
                rowX[c] = 0.5 * (vertexBasis[s, c] + vertexBasis[next, c]);
                rowY[c] = 0.5 * (vertexBasis[nv + s, c] + vertexBasis[nv + next, c]);
            }

            midpoints[s] = new PlanAnchor(rowX, rowY, OuterSum(rowX, rowY, md));
        }

        // A layout with one interior point per edge exposes every run the template has,
        // which is all that is needed to read off the parts that do not depend on k.
        var probe = new int[template.KVariableCount];
        Array.Fill(probe, 2);
        TileLayout probeLayout = TileLayout.Create(template, probe);

        int slotCount = 0;
        foreach (EdgeRun run in probeLayout.Runs)
        {
            slotCount = Math.Max(slotCount, run.Slot + 1);
        }

        var runs = new PlanRun[slotCount];
        foreach (EdgeRun run in probeLayout.Runs)
        {
            runs[run.Slot] = BuildRun(run.Slot, run, vertexBasis, template, md);
        }

        return new TemplatePlan(template, vertexBasis, vertices, midpoints, runs, vertexGram);
    }

    /// <summary>Builds the k independent part of one run.</summary>
    /// <param name="slot">The run index.</param>
    /// <param name="run">The run as the probe layout describes it.</param>
    /// <param name="vertexBasis">The vertex parametrisation.</param>
    /// <param name="template">The template.</param>
    /// <param name="md">The number of vertex columns.</param>
    /// <returns>The plan run.</returns>
    private static PlanRun BuildRun(
        int slot,
        EdgeRun run,
        DenseMatrix vertexBasis,
        TemplateSpec template,
        int md)
    {
        int nv = template.VertexCount;

        // The fast evaluator needs P to be the ascending index, so a run whose P descends
        // exchanges its two sides; the forms and the basis rows move with them.
        bool swap = run.PStep < 0;
        double[] lx = Reorder(run.Lx, swap);
        double[] ly = Reorder(run.Ly, swap);
        BasisPoint pBasis = swap ? run.QBasis : run.PBasis;
        BasisPoint qBasis = swap ? run.PBasis : run.QBasis;

        double inverseSqrt2 = 1.0 / Math.Sqrt(2.0);
        var scaledX = new double[4];
        var scaledY = new double[4];
        for (int i = 0; i < 4; i++)
        {
            scaledX[i] = lx[i] * inverseSqrt2;
            scaledY[i] = ly[i] * inverseSqrt2;
        }

        // J maps z to (y_p, -x_p, y_q, -x_q), so J transposed maps a form the same way
        // with the signs on the other half.
        var jlx = new double[] { -scaledX[1], scaledX[0], -scaledX[3], scaledX[2] };
        var jly = new double[] { -scaledY[1], scaledY[0], -scaledY[3], scaledY[2] };

        // The four rows of the block, before and after projecting the edge columns out.
        var block = new double[4 * md];
        WriteAnchor(block, 0 * md, pBasis, vertexBasis, nv, md, wantY: false);
        WriteAnchor(block, 1 * md, pBasis, vertexBasis, nv, md, wantY: true);
        WriteAnchor(block, 2 * md, qBasis, vertexBasis, nv, md, wantY: false);
        WriteAnchor(block, 3 * md, qBasis, vertexBasis, nv, md, wantY: true);

        foreach (double[] form in (double[][])[lx, ly])
        {
            for (int c = 0; c < md; c++)
            {
                double dot = 0;
                for (int i = 0; i < 4; i++)
                {
                    dot += form[i] * block[(i * md) + c];
                }

                dot *= 0.5;
                for (int i = 0; i < 4; i++)
                {
                    block[(i * md) + c] -= form[i] * dot;
                }
            }
        }

        // X' transposed times X', which the Gram matrix needs scaled by the run length.
        var blockGram = new double[md * md];
        for (int i = 0; i < 4; i++)
        {
            for (int a = 0; a < md; a++)
            {
                double value = block[(i * md) + a];
                if (value == 0)
                {
                    continue;
                }

                for (int b = 0; b < md; b++)
                {
                    blockGram[(a * md) + b] += value * block[(i * md) + b];
                }
            }
        }

        return new PlanRun(
            slot, run.Kind, run.EdgeA, run.EdgeB, run.IsDiagonal, swap,
            scaledX, scaledY, jlx, jly, block, blockGram);
    }

    /// <summary>Exchanges the two halves of a four entry form.</summary>
    /// <param name="form">The form.</param>
    /// <param name="swap">Whether to exchange.</param>
    /// <returns>The form, exchanged or copied.</returns>
    private static double[] Reorder(double[] form, bool swap) =>
        swap ? [form[2], form[3], form[0], form[1]] : [.. form];

    /// <summary>Writes the row of one coordinate of a basis point into a block.</summary>
    /// <param name="block">The destination.</param>
    /// <param name="offset">Where the row starts.</param>
    /// <param name="basis">The basis point.</param>
    /// <param name="vertexBasis">The vertex parametrisation.</param>
    /// <param name="nv">The number of vertices.</param>
    /// <param name="md">The number of vertex columns.</param>
    /// <param name="wantY">Whether the y row is wanted rather than the x row.</param>
    private static void WriteAnchor(
        double[] block,
        int offset,
        BasisPoint basis,
        DenseMatrix vertexBasis,
        int nv,
        int md,
        bool wantY)
    {
        int first = basis.VertexA + (wantY ? nv : 0);
        if (!basis.IsMidpoint)
        {
            for (int c = 0; c < md; c++)
            {
                block[offset + c] = vertexBasis[first, c];
            }

            return;
        }

        int second = basis.VertexB + (wantY ? nv : 0);
        for (int c = 0; c < md; c++)
        {
            block[offset + c] = 0.5 * (vertexBasis[first, c] + vertexBasis[second, c]);
        }
    }

    /// <summary>The sum of the outer products of two rows with themselves.</summary>
    /// <param name="rowX">The first row.</param>
    /// <param name="rowY">The second row.</param>
    /// <param name="md">The row length.</param>
    /// <returns>The md by md matrix, row major.</returns>
    private static double[] OuterSum(double[] rowX, double[] rowY, int md)
    {
        var gram = new double[md * md];
        for (int a = 0; a < md; a++)
        {
            for (int b = 0; b < md; b++)
            {
                gram[(a * md) + b] = (rowX[a] * rowX[b]) + (rowY[a] * rowY[b]);
            }
        }

        return gram;
    }
}
