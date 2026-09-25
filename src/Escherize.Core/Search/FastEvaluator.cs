using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Escherize.Parametrization;

namespace Escherize.Search;

/// <summary>
/// The fast evaluator of SPEC §6.3: the distance for one k vector and one start offset in
/// time that does not grow with n.
/// </summary>
/// <remarks>
/// One instance belongs to one template and one goal orientation, and is pointed at a k
/// vector with <see cref="Load"/>. Every buffer is allocated in the constructor, so
/// neither loading a k vector nor evaluating an offset allocates; the inner loop also
/// calls nothing virtual and uses no LINQ, as SPEC §0.6 requires.
/// </remarks>
public sealed class FastEvaluator
{
    private readonly GoalTables _tables;
    private readonly TemplatePlan _template;
    private readonly bool _procrustes;
    private readonly int _n;
    private readonly int _md;

    // The md loops are the bulk of the work, so the buffers they walk are padded to a
    // whole number of vector lanes. The padding is zero, so the extra lanes add nothing
    // and the loops need no scalar tail.
    private readonly int _mdPadded;

    // Template level tables, indexed by run slot. These never change.
    private readonly double[] _slotLx;
    private readonly double[] _slotLy;
    private readonly double[] _slotJLx;
    private readonly double[] _slotJLy;
    private readonly double[] _slotBlock;
    private readonly bool[] _slotDiagonal;

    // Per slot, the ten coefficients that turn the distinct entries of the moment matrix
    // into the three edge sums. They replace four by four forms with a dot product.
    private readonly double[] _slotWw;
    private readonly double[] _slotWwc;
    private readonly double[] _slotWcwc;

    // The goal tables as plain arrays, read straight in the hot loop.
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _prefix;
    private readonly double[] _anti;
    private readonly double[] _diag;
    private readonly int _stride;

    private readonly int _vertexCount;
    private readonly int[] _vertexPoint;
    private readonly double[] _vertexRows;

    // Per k vector state, refilled by Load.
    private int _runCount;
    private readonly int[] _runSlot;
    private readonly int[] _runPStart;
    private readonly int[] _runQStart;
    private readonly int[] _runLength;

    private int _midpointCount;
    private readonly int[] _midpointPoint;
    private readonly double[] _midpointRows;

    private readonly double[] _cholesky;

    // Scratch buffers.
    private readonly double[] _g;
    private readonly double[] _gc;
    private readonly double[] _c;
    private readonly double[] _cc;
    private readonly double[] _moment = new double[16];

    /// <summary>Creates an evaluator for one template and one goal orientation.</summary>
    /// <param name="template">The template plan.</param>
    /// <param name="tables">The tables of the goal orientation.</param>
    public FastEvaluator(TemplatePlan template, GoalTables tables)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(tables);

        _template = template;
        _tables = tables;
        _n = tables.PointCount;
        _md = template.Md;
        _mdPadded = ((_md + Vector<double>.Count - 1) / Vector<double>.Count) * Vector<double>.Count;
        _procrustes = template.Template.UsesProcrustes;

        int slots = template.Runs.Length;
        int nv = template.Template.VertexCount;

        _slotLx = new double[4 * slots];
        _slotLy = new double[4 * slots];
        _slotJLx = new double[4 * slots];
        _slotJLy = new double[4 * slots];
        _slotBlock = new double[4 * _mdPadded * slots];
        _slotDiagonal = new bool[slots];
        _slotWw = new double[10 * slots];
        _slotWwc = new double[10 * slots];
        _slotWcwc = new double[10 * slots];

        (_x, _y, _prefix, _anti, _diag) = tables.Raw();
        _stride = tables.Stride;

        for (int slot = 0; slot < slots; slot++)
        {
            PlanRun run = template.Runs[slot];
            run.Lx.CopyTo(_slotLx, 4 * slot);
            run.Ly.CopyTo(_slotLy, 4 * slot);
            run.JLx.CopyTo(_slotJLx, 4 * slot);
            run.JLy.CopyTo(_slotJLy, 4 * slot);
            for (int row = 0; row < 4; row++)
            {
                Array.Copy(run.Block, row * _md, _slotBlock, (4 * _mdPadded * slot) + (row * _mdPadded), _md);
            }

            _slotDiagonal[slot] = run.Diagonal;

            AddMomentCoefficients(_slotWw, 10 * slot, run.Lx, run.Lx);
            AddMomentCoefficients(_slotWw, 10 * slot, run.Ly, run.Ly);
            AddMomentCoefficients(_slotWwc, 10 * slot, run.Lx, run.JLx);
            AddMomentCoefficients(_slotWwc, 10 * slot, run.Ly, run.JLy);
            AddMomentCoefficients(_slotWcwc, 10 * slot, run.JLx, run.JLx);
            AddMomentCoefficients(_slotWcwc, 10 * slot, run.JLy, run.JLy);
        }

        _vertexCount = nv;
        _vertexPoint = new int[nv];
        _vertexRows = new double[2 * _mdPadded * nv];
        for (int s = 0; s < nv; s++)
        {
            template.Vertices[s].RowX.CopyTo(_vertexRows, 2 * _mdPadded * s);
            template.Vertices[s].RowY.CopyTo(_vertexRows, (2 * _mdPadded * s) + _mdPadded);
        }

        _runSlot = new int[slots];
        _runPStart = new int[slots];
        _runQStart = new int[slots];
        _runLength = new int[slots];

        _midpointPoint = new int[nv];
        _midpointRows = new double[2 * _mdPadded * nv];

        _cholesky = new double[_md * _md];
        _g = new double[_mdPadded];
        _gc = new double[_mdPadded];
        _c = new double[_mdPadded];
        _cc = new double[_mdPadded];
    }

    /// <summary>Creates an evaluator already pointed at one k vector.</summary>
    /// <param name="plan">The basis plan.</param>
    /// <param name="tables">The tables of the goal orientation.</param>
    public FastEvaluator(BasisPlan plan, GoalTables tables)
        : this((plan ?? throw new ArgumentNullException(nameof(plan))).Template, tables) =>
        Load(plan);

    /// <summary>The template this evaluator belongs to.</summary>
    public TemplatePlan Template => _template;

    /// <summary>
    /// Points the evaluator at another k vector of the same template, without allocating.
    /// </summary>
    /// <param name="plan">The basis plan.</param>
    /// <exception cref="ArgumentException">The plan belongs to another template.</exception>
    public void Load(BasisPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(plan.Template, _template))
        {
            throw new ArgumentException("The plan belongs to a different template.", nameof(plan));
        }

        _runCount = plan.Placements.Length;
        for (int r = 0; r < _runCount; r++)
        {
            RunPlacement placement = plan.Placements[r];
            _runSlot[r] = placement.Slot;
            _runPStart[r] = placement.PStart;
            _runQStart[r] = placement.QStart;
            _runLength[r] = placement.Length;
        }

        for (int s = 0; s < _vertexCount; s++)
        {
            _vertexPoint[s] = plan.VertexPoints[s];
        }

        _midpointCount = plan.MidpointPoints.Length;
        for (int i = 0; i < _midpointCount; i++)
        {
            _midpointPoint[i] = plan.MidpointPoints[i];
            PlanAnchor anchor = _template.EdgeMidpoints[plan.MidpointEdges[i]]!;
            anchor.RowX.CopyTo(_midpointRows, 2 * _mdPadded * i);
            anchor.RowY.CopyTo(_midpointRows, (2 * _mdPadded * i) + _mdPadded);
        }

        for (int a = 0; a < _md; a++)
        {
            for (int b = 0; b <= a; b++)
            {
                _cholesky[(a * _md) + b] = plan.Cholesky[a, b];
            }
        }
    }

    /// <summary>
    /// The distance measure for one start offset (SPEC §6.1, §6.3). The goal has unit
    /// norm and a shift only permutes it, so the norm term is one.
    /// </summary>
    /// <param name="j">The start offset.</param>
    /// <returns>The distance e.</returns>
    public double Evaluate(int j) => _procrustes ? EvaluateProcrustes(j) : EvaluateEuclid(j);

    /// <summary>The euclidean case, for a template without glide pairs (SPEC §6.1).</summary>
    /// <param name="j">The start offset.</param>
    /// <returns>The distance e.</returns>
    private double EvaluateEuclid(int j)
    {
        double[] g = _g;
        Array.Clear(g);
        double sww = 0;

        for (int r = 0; r < _runCount; r++)
        {
            int slot = _runSlot[r];
            Span<double> m = _moment;
            (double sumXp, double sumYp, double sumXq, double sumYq) = Moments(r, slot, _runLength[r], j, m);

            int coefficients = 10 * slot;
            for (int i = 0; i < 10; i++)
            {
                sww += _slotWw[coefficients + i] * m[i];
            }

            AddRunToVertexPart(4 * _mdPadded * slot, sumXp, sumYp, sumXq, sumYq);
        }

        AddAnchorsToVertexPart(j);

        return Math.Max(0, 1.0 - (ForwardSolveNormSquared(g, _c) + sww));
    }

    /// <summary>The Procrustes case, where the rotation is free (SPEC §6.1).</summary>
    /// <param name="j">The start offset.</param>
    /// <returns>The distance e.</returns>
    private double EvaluateProcrustes(int j)
    {
        double[] g = _g;
        double[] gc = _gc;
        Array.Clear(g);
        Array.Clear(gc);
        double sww = 0;
        double swwc = 0;
        double swcwc = 0;

        for (int r = 0; r < _runCount; r++)
        {
            int slot = _runSlot[r];
            Span<double> m = _moment;
            (double sumXp, double sumYp, double sumXq, double sumYq) = Moments(r, slot, _runLength[r], j, m);

            int coefficients = 10 * slot;
            for (int i = 0; i < 10; i++)
            {
                double value = m[i];
                sww += _slotWw[coefficients + i] * value;
                swwc += _slotWwc[coefficients + i] * value;
                swcwc += _slotWcwc[coefficients + i] * value;
            }

            AddRunToBothParts(4 * _mdPadded * slot, sumXp, sumYp, sumXq, sumYq);
        }

        AddAnchorsToBothParts(j);

        double g11 = ForwardSolveNormSquared(g, _c) + sww;
        double g22 = ForwardSolveNormSquared(gc, _cc) + swcwc;

        double cross = 0;
        for (int c = 0; c < _md; c++)
        {
            cross += _c[c] * _cc[c];
        }

        double g12 = cross + swwc;
        double half = (g11 - g22) / 2;
        double lambda = ((g11 + g22) / 2) + Math.Sqrt((half * half) + (g12 * g12));
        return Math.Max(0, 1.0 - lambda);
    }

    /// <summary>
    /// Adds one run to the vertex part: X' transposed times the four coordinate sums
    /// (SPEC §6.3 step 2).
    /// </summary>
    /// <param name="block">Where the projected block of this run starts.</param>
    /// <param name="sumXp">The sum of x over the P side.</param>
    /// <param name="sumYp">The sum of y over the P side.</param>
    /// <param name="sumXq">The sum of x over the Q side.</param>
    /// <param name="sumYq">The sum of y over the Q side.</param>
    private void AddRunToVertexPart(int block, double sumXp, double sumYp, double sumXq, double sumYq)
    {
        double[] source = _slotBlock;
        double[] g = _g;
        int md = _mdPadded;
        int width = Vector<double>.Count;

        var xp = new Vector<double>(sumXp);
        var yp = new Vector<double>(sumYp);
        var xq = new Vector<double>(sumXq);
        var yq = new Vector<double>(sumYq);

        for (int c = 0; c < md; c += width)
        {
            var accumulator = new Vector<double>(g, c);
            accumulator += new Vector<double>(source, block + c) * xp;
            accumulator += new Vector<double>(source, block + md + c) * yp;
            accumulator += new Vector<double>(source, block + (2 * md) + c) * xq;
            accumulator += new Vector<double>(source, block + (3 * md) + c) * yq;
            accumulator.CopyTo(g, c);
        }
    }

    /// <summary>The same for both the goal and the goal turned by minus ninety degrees.</summary>
    /// <param name="block">Where the projected block of this run starts.</param>
    /// <param name="sumXp">The sum of x over the P side.</param>
    /// <param name="sumYp">The sum of y over the P side.</param>
    /// <param name="sumXq">The sum of x over the Q side.</param>
    /// <param name="sumYq">The sum of y over the Q side.</param>
    private void AddRunToBothParts(int block, double sumXp, double sumYp, double sumXq, double sumYq)
    {
        double[] source = _slotBlock;
        double[] g = _g;
        double[] gc = _gc;
        int md = _mdPadded;
        int width = Vector<double>.Count;

        var xp = new Vector<double>(sumXp);
        var yp = new Vector<double>(sumYp);
        var xq = new Vector<double>(sumXq);
        var yq = new Vector<double>(sumYq);

        for (int c = 0; c < md; c += width)
        {
            var b0 = new Vector<double>(source, block + c);
            var b1 = new Vector<double>(source, block + md + c);
            var b2 = new Vector<double>(source, block + (2 * md) + c);
            var b3 = new Vector<double>(source, block + (3 * md) + c);

            (new Vector<double>(g, c) + (b0 * xp) + (b1 * yp) + (b2 * xq) + (b3 * yq)).CopyTo(g, c);

            // The rotated goal reuses the same sums: (x, y) becomes (y, -x).
            (new Vector<double>(gc, c) + (b0 * yp) - (b1 * xp) + (b2 * yq) - (b3 * xq)).CopyTo(gc, c);
        }
    }

    /// <summary>
    /// Adds the tiling vertices and the pinned midpoints to the vertex part
    /// (SPEC §6.3 step 2).
    /// </summary>
    /// <param name="j">The start offset.</param>
    private void AddAnchorsToVertexPart(int j)
    {
        double[] g = _g;
        int md = _mdPadded;
        int width = Vector<double>.Count;

        for (int s = 0; s < _vertexCount; s++)
        {
            AddAnchor(g, _vertexRows, 2 * md * s, _vertexPoint[s] + j, md, width);
        }

        for (int i = 0; i < _midpointCount; i++)
        {
            AddAnchor(g, _midpointRows, 2 * md * i, _midpointPoint[i] + j, md, width);
        }
    }

    /// <summary>The same for both the goal and the goal turned by minus ninety degrees.</summary>
    /// <param name="j">The start offset.</param>
    private void AddAnchorsToBothParts(int j)
    {
        int md = _mdPadded;
        int width = Vector<double>.Count;

        for (int s = 0; s < _vertexCount; s++)
        {
            AddAnchorBoth(_vertexRows, 2 * md * s, _vertexPoint[s] + j, md, width);
        }

        for (int i = 0; i < _midpointCount; i++)
        {
            AddAnchorBoth(_midpointRows, 2 * md * i, _midpointPoint[i] + j, md, width);
        }
    }

    /// <summary>Adds one pinned point to the vertex part.</summary>
    /// <param name="g">The accumulator.</param>
    /// <param name="rows">The array holding the two rows of the point.</param>
    /// <param name="offset">Where the rows start.</param>
    /// <param name="index">The goal index of the point.</param>
    /// <param name="md">The padded number of vertex columns.</param>
    /// <param name="width">The number of lanes in a vector.</param>
    private void AddAnchor(double[] g, double[] rows, int offset, int index, int md, int width)
    {
        var px = new Vector<double>(_x[index]);
        var py = new Vector<double>(_y[index]);

        for (int c = 0; c < md; c += width)
        {
            var accumulator = new Vector<double>(g, c);
            accumulator += new Vector<double>(rows, offset + c) * px;
            accumulator += new Vector<double>(rows, offset + md + c) * py;
            accumulator.CopyTo(g, c);
        }
    }

    /// <summary>Adds one pinned point to both accumulators.</summary>
    /// <param name="rows">The array holding the two rows of the point.</param>
    /// <param name="offset">Where the rows start.</param>
    /// <param name="index">The goal index of the point.</param>
    /// <param name="md">The padded number of vertex columns.</param>
    /// <param name="width">The number of lanes in a vector.</param>
    private void AddAnchorBoth(double[] rows, int offset, int index, int md, int width)
    {
        double[] g = _g;
        double[] gc = _gc;
        var px = new Vector<double>(_x[index]);
        var py = new Vector<double>(_y[index]);

        for (int c = 0; c < md; c += width)
        {
            var rx = new Vector<double>(rows, offset + c);
            var ry = new Vector<double>(rows, offset + md + c);
            (new Vector<double>(g, c) + (rx * px) + (ry * py)).CopyTo(g, c);
            (new Vector<double>(gc, c) + (rx * py) - (ry * px)).CopyTo(gc, c);
        }
    }

    /// <summary>
    /// Fills the ten distinct entries of the moment matrix of one run and returns the four
    /// coordinate sums it also needs (SPEC §6.3 step 1).
    /// </summary>
    /// <param name="r">The index of the run within the loaded k vector.</param>
    /// <param name="slot">The run slot.</param>
    /// <param name="length">The run length.</param>
    /// <param name="j">The start offset.</param>
    /// <param name="m">Receives the entries, in the order 00, 01, 02, 03, 11, 12, 13, 22, 23, 33.</param>
    /// <returns>The sums of x and y over the P and the Q side.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (double SumXp, double SumYp, double SumXq, double SumYq) Moments(
        int r, int slot, int length, int j, Span<double> m)
    {
        int n = _n;

        // Both index sequences are reduced into the first period, so the ranges stay
        // inside the doubled arrays (SPEC §6.3 step 1).
        int p0 = _runPStart[r] + j;
        p0 -= p0 >= n ? n : 0;
        int q0 = _runQStart[r] + j;
        q0 -= q0 >= n ? n : 0;

        // The five prefix sums of one index sit together, so each end of a range is a
        // single neighbouring read rather than five scattered ones.
        int pLow = p0 * GoalTables.PrefixLanes;
        int pHigh = (p0 + length) * GoalTables.PrefixLanes;
        double sumXp = _prefix[pHigh + 0] - _prefix[pLow + 0];
        double sumYp = _prefix[pHigh + 1] - _prefix[pLow + 1];
        m[0] = _prefix[pHigh + 2] - _prefix[pLow + 2];
        m[4] = _prefix[pHigh + 3] - _prefix[pLow + 3];
        m[1] = _prefix[pHigh + 4] - _prefix[pLow + 4];

        bool diagonal = _slotDiagonal[slot];
        int qStart = diagonal ? q0 : q0 - length + 1;
        qStart += qStart < 0 ? n : 0;
        int qLow = qStart * GoalTables.PrefixLanes;
        int qHigh = (qStart + length) * GoalTables.PrefixLanes;
        double sumXq = _prefix[qHigh + 0] - _prefix[qLow + 0];
        double sumYq = _prefix[qHigh + 1] - _prefix[qLow + 1];
        m[7] = _prefix[qHigh + 2] - _prefix[qLow + 2];
        m[9] = _prefix[qHigh + 3] - _prefix[qLow + 3];
        m[8] = _prefix[qHigh + 4] - _prefix[qLow + 4];

        // The cross terms come from whichever of the two tables matches the direction the
        // run travels in; the four products of one position are adjacent.
        double[] table;
        int key;
        if (diagonal)
        {
            // q(i) = p(i) + d, so the difference of the two indices is fixed.
            table = _diag;
            key = q0 - p0;
            key += key < 0 ? n : 0;
        }
        else
        {
            // q(i) = s - p(i), so the sum of the two indices is fixed.
            table = _anti;
            key = p0 + q0;
            key -= key >= n ? n : 0;
        }

        int rowLow = (((key * _stride) + p0) * GoalTables.Lanes);
        int rowHigh = rowLow + (length * GoalTables.Lanes);
        m[2] = table[rowHigh + 0] - table[rowLow + 0];
        m[3] = table[rowHigh + 1] - table[rowLow + 1];
        m[5] = table[rowHigh + 2] - table[rowLow + 2];
        m[6] = table[rowHigh + 3] - table[rowLow + 3];

        return (sumXp, sumYp, sumXq, sumYq);
    }

    /// <summary>
    /// Adds the coefficients of a transposed times M times b to a slot, expressed over the
    /// ten distinct entries of the symmetric moment matrix.
    /// </summary>
    /// <param name="target">The coefficient array.</param>
    /// <param name="offset">Where this slot starts.</param>
    /// <param name="a">The first four entry form.</param>
    /// <param name="b">The second four entry form.</param>
    private static void AddMomentCoefficients(double[] target, int offset, double[] a, double[] b)
    {
        target[offset + 0] += a[0] * b[0];
        target[offset + 1] += (a[0] * b[1]) + (a[1] * b[0]);
        target[offset + 2] += (a[0] * b[2]) + (a[2] * b[0]);
        target[offset + 3] += (a[0] * b[3]) + (a[3] * b[0]);
        target[offset + 4] += a[1] * b[1];
        target[offset + 5] += (a[1] * b[2]) + (a[2] * b[1]);
        target[offset + 6] += (a[1] * b[3]) + (a[3] * b[1]);
        target[offset + 7] += a[2] * b[2];
        target[offset + 8] += (a[2] * b[3]) + (a[3] * b[2]);
        target[offset + 9] += a[3] * b[3];
    }

    /// <summary>
    /// Solves L c = g by forward substitution and returns the squared norm of c
    /// (SPEC §6.3 step 2).
    /// </summary>
    /// <param name="g">The right hand side.</param>
    /// <param name="c">Receives the solution.</param>
    /// <returns>The squared norm of the solution.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double ForwardSolveNormSquared(ReadOnlySpan<double> g, double[] c)
    {
        int md = _md;
        ref double l = ref MemoryMarshal.GetArrayDataReference(_cholesky);
        ref double target = ref MemoryMarshal.GetArrayDataReference(c);

        double total = 0;
        for (int i = 0; i < md; i++)
        {
            double sum = g[i];
            int row = i * md;
            for (int p = 0; p < i; p++)
            {
                sum -= Unsafe.Add(ref l, row + p) * Unsafe.Add(ref target, p);
            }

            double value = sum / Unsafe.Add(ref l, row + i);
            Unsafe.Add(ref target, i) = value;
            total += value * value;
        }

        return total;
    }

}
