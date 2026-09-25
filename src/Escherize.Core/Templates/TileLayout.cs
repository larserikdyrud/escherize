using Escherize.Geometry;

namespace Escherize.Templates;

/// <summary>
/// The point that a run is anchored on before the free edge parameters are added
/// (SPEC §5.5 step 1). It is either a single vertex or the midpoint of an edge.
/// </summary>
/// <param name="VertexA">The vertex, or the first of the two whose midpoint is taken.</param>
/// <param name="VertexB">The second vertex, or -1 when the basis is a single vertex.</param>
public readonly record struct BasisPoint(int VertexA, int VertexB)
{
    /// <summary>A basis that is one tiling vertex.</summary>
    /// <param name="vertex">The vertex index.</param>
    /// <returns>The basis point.</returns>
    public static BasisPoint Vertex(int vertex) => new(vertex, -1);

    /// <summary>A basis that is the midpoint of the two given vertices.</summary>
    /// <param name="first">The first vertex.</param>
    /// <param name="second">The second vertex.</param>
    /// <returns>The basis point.</returns>
    public static BasisPoint Midpoint(int first, int second) => new(first, second);

    /// <summary>Whether the basis is the midpoint of two vertices.</summary>
    public bool IsMidpoint => VertexB >= 0;
}

/// <summary>
/// One run of interior point pairs for a single relation (SPEC §5.4). The pair for index
/// i is (P(i), Q(i)); both index sequences are arithmetic, so a run is described by a
/// start and a step.
/// </summary>
/// <param name="Kind">The relation kind.</param>
/// <param name="EdgeA">The first edge of the relation, or the edge itself for a C run.</param>
/// <param name="EdgeB">The second edge of the relation, or the edge itself for a C run.</param>
/// <param name="Length">The number of index values, k for a pair and floor(k/2) for a C run.</param>
/// <param name="PStart">The point index of P(1).</param>
/// <param name="PStep">The step of the P index, +1 or -1.</param>
/// <param name="QStart">The point index of Q(1).</param>
/// <param name="QStep">The step of the Q index, +1 or -1.</param>
/// <param name="Lx">The linear form of the x column over (x_p, y_p, x_q, y_q), before scaling.</param>
/// <param name="Ly">The linear form of the y column, before scaling.</param>
/// <param name="PBasis">The basis point of the P side.</param>
/// <param name="QBasis">The basis point of the Q side.</param>
/// <param name="ColumnStart">The index of the first of this run's columns within the edge block.</param>
/// <param name="Slot">
/// The position of this run among all the runs the template can have, counting the runs a
/// small k leaves out. It lets the parametrisation index the template tables directly.
/// </param>
public sealed record EdgeRun(
    EdgeKind Kind,
    int EdgeA,
    int EdgeB,
    int Length,
    int PStart,
    int PStep,
    int QStart,
    int QStep,
    double[] Lx,
    double[] Ly,
    BasisPoint PBasis,
    BasisPoint QBasis,
    int ColumnStart,
    int Slot)
{
    /// <summary>The point index of P(i).</summary>
    /// <param name="i">The one based index within the run.</param>
    /// <returns>The point index.</returns>
    public int P(int i) => PStart + ((i - 1) * PStep);

    /// <summary>The point index of Q(i).</summary>
    /// <param name="i">The one based index within the run.</param>
    /// <returns>The point index.</returns>
    public int Q(int i) => QStart + ((i - 1) * QStep);

    /// <summary>
    /// Whether P and Q run in the same direction. Glide runs are diagonal; C, T and R runs
    /// are anti-diagonal (SPEC §5.4).
    /// </summary>
    public bool IsDiagonal => PStep == QStep;
}

/// <summary>
/// The index bookkeeping for one template and one k vector (SPEC §3, §5.4, §5.6).
/// </summary>
public sealed class TileLayout
{
    private readonly int[] _h;
    private readonly int[] _edgeK;
    private readonly EdgeRun[] _runs;
    private readonly int[] _cMidpointIndices;
    private readonly int[] _cMidpointEdges;

    private TileLayout(
        TemplateSpec template,
        int[] k,
        int[] edgeK,
        int[] h,
        EdgeRun[] runs,
        int edgeColumnCount,
        int[] cMidpointIndices,
        int[] cMidpointEdges)
    {
        Template = template;
        K = k;
        _edgeK = edgeK;
        _h = h;
        _runs = runs;
        EdgeColumnCount = edgeColumnCount;
        _cMidpointIndices = cMidpointIndices;
        _cMidpointEdges = cMidpointEdges;
    }

    /// <summary>The template this layout belongs to.</summary>
    public TemplateSpec Template { get; }

    /// <summary>The k vector, indexed by k variable (SPEC §5.6).</summary>
    public int[] K { get; }

    /// <summary>The number of tiling vertices, nv.</summary>
    public int VertexCount => Template.VertexCount;

    /// <summary>The total number of tile points, n.</summary>
    public int PointCount => _h[VertexCount];

    /// <summary>The number of columns contributed by the edge parameters, ms (SPEC §5.4).</summary>
    public int EdgeColumnCount { get; }

    /// <summary>The runs of the layout (SPEC §5.4).</summary>
    public IReadOnlyList<EdgeRun> Runs => _runs;

    /// <summary>The point indices of the fixed midpoints of odd length C edges (SPEC §5.5).</summary>
    public IReadOnlyList<int> CMidpointIndices => _cMidpointIndices;

    /// <summary>The edges those midpoints belong to, in the same order.</summary>
    public IReadOnlyList<int> CMidpointEdges => _cMidpointEdges;

    /// <summary>The index of vertex V_s within the point list, h(s) (SPEC §3).</summary>
    /// <param name="s">The vertex index.</param>
    /// <returns>The point index.</returns>
    public int VertexIndex(int s) => _h[s % VertexCount];

    /// <summary>The number of interior points on edge s.</summary>
    /// <param name="s">The edge index.</param>
    /// <returns>The interior point count k_s.</returns>
    public int EdgeK(int s) => _edgeK[s];

    /// <summary>
    /// The point index of e_s(i), for i from 0 to k_s + 1, where e_s(0) is V_s and
    /// e_s(k_s + 1) is V_(s+1) (SPEC §3).
    /// </summary>
    /// <param name="s">The edge index.</param>
    /// <param name="i">The position along the edge.</param>
    /// <returns>The point index.</returns>
    public int EdgePointIndex(int s, int i) => (_h[s] + i) % PointCount;

    /// <summary>Builds the layout for a template and a k vector.</summary>
    /// <param name="template">The template.</param>
    /// <param name="k">The k vector, one entry per k variable.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentException">The k vector has the wrong length or a negative entry.</exception>
    public static TileLayout Create(TemplateSpec template, ReadOnlySpan<int> k)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (k.Length != template.KVariableCount)
        {
            throw new ArgumentException(
                $"Template {template.Name} has {template.KVariableCount} k variables, not {k.Length}.",
                nameof(k));
        }

        foreach (int value in k)
        {
            if (value < 0)
            {
                throw new ArgumentException("A k value cannot be negative.", nameof(k));
            }
        }

        int nv = template.VertexCount;
        var edgeK = new int[nv];
        var h = new int[nv + 1];
        for (int s = 0; s < nv; s++)
        {
            edgeK[s] = k[template.Edges[s].KVar];
            h[s + 1] = h[s] + edgeK[s] + 1;
        }

        var runs = new List<EdgeRun>();
        var midpointIndices = new List<int>();
        var midpointEdges = new List<int>();
        int columns = 0;

        // The slot counter advances for every run the template can have, whether or not
        // this k vector is large enough to produce it.
        int slot = 0;

        // C edges pair their own interior points about the edge midpoint.
        for (int s = 0; s < nv; s++)
        {
            if (template.Edges[s].Kind != EdgeKind.C)
            {
                continue;
            }

            int ks = edgeK[s];
            int length = ks / 2;
            BasisPoint basis = BasisPoint.Midpoint(s, (s + 1) % nv);

            if (length > 0)
            {
                runs.Add(new EdgeRun(
                    EdgeKind.C, s, s, length,
                    PStart: h[s] + 1, PStep: 1,
                    QStart: h[s] + ks, QStep: -1,
                    Lx: [1, 0, -1, 0],
                    Ly: [0, 1, 0, -1],
                    PBasis: basis,
                    QBasis: basis,
                    ColumnStart: columns,
                    Slot: slot));
                columns += 2 * length;
            }

            slot++;

            if (ks % 2 == 1)
            {
                midpointIndices.Add(h[s] + ((ks + 1) / 2));
                midpointEdges.Add(s);
            }
        }

        // Paired edges relate their interior points to those of the partner edge.
        foreach ((int a, int b) in template.Pairs())
        {
            EdgeSpec edge = template.Edges[a];
            int ka = edgeK[a];
            if (ka == 0)
            {
                slot++;
                continue;
            }

            EdgeRun run = edge.Kind switch
            {
                EdgeKind.T => new EdgeRun(
                    EdgeKind.T, a, b, ka,
                    PStart: h[a] + 1, PStep: 1,
                    QStart: h[b] + ka, QStep: -1,
                    Lx: [1, 0, 1, 0],
                    Ly: [0, 1, 0, 1],
                    PBasis: BasisPoint.Vertex(a),
                    QBasis: BasisPoint.Vertex((b + 1) % nv),
                    ColumnStart: columns,
                    Slot: slot),

                EdgeKind.G => BuildGlideRun(edge, a, b, ka, h, nv, columns, slot),

                EdgeKind.R => BuildRotationRun(edge, a, b, ka, h, nv, columns, slot),

                _ => throw new InvalidOperationException($"Edge kind {edge.Kind} cannot be a pair."),
            };

            runs.Add(run);
            columns += 2 * ka;
            slot++;
        }

        return new TileLayout(
            template, k.ToArray(), edgeK, h, [.. runs], columns, [.. midpointIndices], [.. midpointEdges]);
    }

    /// <summary>Builds the run of a glide pair (SPEC §5.4).</summary>
    /// <param name="edge">The edge specification, which carries the axis.</param>
    /// <param name="a">The first edge index.</param>
    /// <param name="b">The second edge index.</param>
    /// <param name="k">The number of interior points.</param>
    /// <param name="h">The vertex offsets.</param>
    /// <param name="nv">The number of vertices.</param>
    /// <param name="columnStart">The first column index.</param>
    /// <param name="slot">The run slot.</param>
    /// <returns>The run.</returns>
    private static EdgeRun BuildGlideRun(EdgeSpec edge, int a, int b, int k, int[] h, int nv, int columnStart, int slot)
    {
        (double fx, double fy) = edge.Axis.MirrorFactors();
        return new EdgeRun(
            EdgeKind.G, a, b, k,
            PStart: h[a] + 1, PStep: 1,
            QStart: h[b] + 1, QStep: 1,
            Lx: [1, 0, fx, 0],
            Ly: [0, 1, 0, fy],
            PBasis: BasisPoint.Vertex(a % nv),
            QBasis: BasisPoint.Vertex(b % nv),
            ColumnStart: columnStart,
            Slot: slot);
    }

    /// <summary>Builds the run of a rotation pair (SPEC §5.4).</summary>
    /// <param name="edge">The edge specification, which carries the angle.</param>
    /// <param name="a">The first edge index.</param>
    /// <param name="b">The second edge index, which is a + 1.</param>
    /// <param name="k">The number of interior points.</param>
    /// <param name="h">The vertex offsets.</param>
    /// <param name="nv">The number of vertices.</param>
    /// <param name="columnStart">The first column index.</param>
    /// <param name="slot">The run slot.</param>
    /// <returns>The run.</returns>
    /// <exception cref="InvalidOperationException">The two edges of the pair are not adjacent.</exception>
    private static EdgeRun BuildRotationRun(EdgeSpec edge, int a, int b, int k, int[] h, int nv, int columnStart, int slot)
    {
        if ((a + 1) % nv != b % nv)
        {
            throw new InvalidOperationException(
                $"A rotation pair needs adjacent edges, but got {a} and {b}.");
        }

        double theta = edge.ThetaRadians;
        double cos = Math.Cos(theta);
        double sin = Math.Sin(theta);

        return new EdgeRun(
            EdgeKind.R, a, b, k,
            PStart: h[a] + k, PStep: -1,
            QStart: h[b] + 1, QStep: 1,
            Lx: [1, 0, cos, sin],
            Ly: [0, 1, -sin, cos],
            PBasis: BasisPoint.Vertex(b % nv),
            QBasis: BasisPoint.Vertex(b % nv),
            ColumnStart: columnStart,
            Slot: slot);
    }
}
