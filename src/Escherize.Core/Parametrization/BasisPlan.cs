using Escherize.Templates;

namespace Escherize.Parametrization;

/// <summary>
/// Where one run sits along the boundary for a given k vector, with P chosen as the
/// ascending index (SPEC §6.3 step 1).
/// </summary>
/// <param name="Slot">The run index within the template.</param>
/// <param name="PStart">The template index of the first P point.</param>
/// <param name="QStart">The template index of the first Q point.</param>
/// <param name="Length">The number of index values in the run.</param>
public readonly record struct RunPlacement(int Slot, int PStart, int QStart, int Length);

/// <summary>
/// Everything about one template and one k vector that the evaluators need: where the runs
/// sit, which midpoints exist, and the Cholesky factor of the Gram matrix (SPEC §5.5).
/// </summary>
public sealed class BasisPlan
{
    private BasisPlan(
        TemplatePlan template,
        TileLayout layout,
        RunPlacement[] placements,
        int[] vertexPoints,
        int[] midpointEdges,
        int[] midpointPoints,
        DenseMatrix cholesky,
        int edgeColumnCount)
    {
        Template = template;
        Layout = layout;
        Placements = placements;
        VertexPoints = vertexPoints;
        MidpointEdges = midpointEdges;
        MidpointPoints = midpointPoints;
        Cholesky = cholesky;
        EdgeColumnCount = edgeColumnCount;
    }

    /// <summary>The template plan.</summary>
    public TemplatePlan Template { get; }

    /// <summary>The layout.</summary>
    public TileLayout Layout { get; }

    /// <summary>Where each run sits, in the run order of the template plan.</summary>
    public RunPlacement[] Placements { get; }

    /// <summary>The point index of each tiling vertex.</summary>
    public int[] VertexPoints { get; }

    /// <summary>The edges whose midpoint is pinned, that is the C edges with an odd k.</summary>
    public int[] MidpointEdges { get; }

    /// <summary>The point index of each pinned midpoint, in the same order.</summary>
    public int[] MidpointPoints { get; }

    /// <summary>The Cholesky factor L of the Gram matrix (SPEC §5.5 step 3).</summary>
    public DenseMatrix Cholesky { get; }

    /// <summary>The number of edge columns, ms.</summary>
    public int EdgeColumnCount { get; }

    /// <summary>The number of vertex columns, md.</summary>
    public int Md => Template.Md;

    /// <summary>The total number of columns, m.</summary>
    public int ColumnCount => Md + EdgeColumnCount;

    /// <summary>Builds the plan for a layout.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The plan.</returns>
    public static BasisPlan Create(TileLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        TemplatePlan template = TemplatePlan.For(layout.Template);
        int md = template.Md;
        int nv = layout.VertexCount;

        var vertexPoints = new int[nv];
        for (int s = 0; s < nv; s++)
        {
            vertexPoints[s] = layout.VertexIndex(s);
        }

        // The runs of the layout are in the same order as the runs of the template plan,
        // except that a C edge with fewer than two interior points contributes none.
        var placements = new List<RunPlacement>(template.Runs.Length);
        var gram = new double[md * md];
        template.VertexGram.CopyTo(gram, 0);

        // Each run carries the slot it occupies in the template, so the two descriptions
        // line up directly even when a small k leaves some runs out.
        foreach (EdgeRun run in layout.Runs)
        {
            if (run.Length == 0)
            {
                continue;
            }

            PlanRun planRun = template.Runs[run.Slot];
            int pStart = planRun.SwapSides ? run.QStart : run.PStart;
            int qStart = planRun.SwapSides ? run.PStart : run.QStart;
            placements.Add(new RunPlacement(run.Slot, pStart, qStart, run.Length));

            double[] blockGram = planRun.BlockGram;
            for (int i = 0; i < gram.Length; i++)
            {
                gram[i] += run.Length * blockGram[i];
            }
        }

        var midpointEdges = new List<int>(nv);
        var midpointPoints = new List<int>(nv);
        for (int i = 0; i < layout.CMidpointIndices.Count; i++)
        {
            int edge = layout.CMidpointEdges[i];
            midpointEdges.Add(edge);
            midpointPoints.Add(layout.CMidpointIndices[i]);

            double[] anchorGram = template.EdgeMidpoints[edge]!.Gram;
            for (int g = 0; g < gram.Length; g++)
            {
                gram[g] += anchorGram[g];
            }
        }

        var gramMatrix = new DenseMatrix(md, md);
        for (int a = 0; a < md; a++)
        {
            for (int b = 0; b < md; b++)
            {
                gramMatrix[a, b] = gram[(a * md) + b];
            }
        }

        return new BasisPlan(
            template,
            layout,
            [.. placements],
            vertexPoints,
            [.. midpointEdges],
            [.. midpointPoints],
            gramMatrix.Cholesky(),
            layout.EdgeColumnCount);
    }

    /// <summary>
    /// The point index of the i-th P point of a placement, where i starts at one.
    /// </summary>
    /// <param name="placement">The placement.</param>
    /// <param name="i">The one based index within the run.</param>
    /// <returns>The point index.</returns>
    public static int PPoint(RunPlacement placement, int i) => placement.PStart + i - 1;

    /// <summary>The point index of the i-th Q point of a placement.</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="slot">The run slot, which says whether the run is diagonal.</param>
    /// <param name="i">The one based index within the run.</param>
    /// <returns>The point index.</returns>
    public int QPoint(RunPlacement placement, int slot, int i) =>
        Template.Runs[slot].Diagonal ? placement.QStart + i - 1 : placement.QStart - i + 1;
}
