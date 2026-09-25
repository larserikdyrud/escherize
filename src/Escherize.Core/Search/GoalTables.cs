namespace Escherize.Search;

/// <summary>
/// The lookup tables of SPEC §6.3, built once per goal orientation. They turn the sums a
/// run needs into a pair of table reads, so an evaluation costs the same whatever n is.
/// </summary>
/// <remarks>
/// <para>
/// Coordinates are stored twice, so an index shifted by j never needs a modulo. The
/// anti-diagonal tables answer sums where the two indices move in opposite directions, as
/// they do for a C, T or R run; the diagonal tables answer the glide case, where both move
/// the same way.
/// </para>
/// <para>
/// SPEC §6.3 describes eight separate two dimensional tables. They are stored interleaved
/// instead, four values to a group, because every lookup wants all four at the same
/// position: at n = 120 the tables are about 1.9 MB and do not fit in cache, so the layout
/// decides how many lines a lookup touches. Interleaving turns eight scattered reads per
/// run into two neighbouring ones. The five prefix sums are interleaved for the same
/// reason, padded to a whole cache line. The contents are exactly what the specification
/// defines; only the arrangement differs.
/// </para>
/// </remarks>
public sealed class GoalTables
{
    /// <summary>The number of values in an interleaved anti-diagonal or diagonal group.</summary>
    internal const int Lanes = 4;

    /// <summary>The stride of the interleaved prefix sums, padded to sixty four bytes.</summary>
    internal const int PrefixLanes = 8;

    private readonly double[] _x;
    private readonly double[] _y;

    // Interleaved as [m * PrefixLanes + lane], lanes being x, y, xx, yy, xy.
    private readonly double[] _prefix;

    // Interleaved as [((s * stride) + m) * Lanes + lane], lanes being xx, xy, yx, yy.
    private readonly double[] _anti;
    private readonly double[] _diag;

    /// <summary>Builds the tables for one orientation of the goal.</summary>
    /// <param name="w">The coordinate vector of length 2n (SPEC §3).</param>
    public GoalTables(ReadOnlySpan<double> w)
    {
        int n = w.Length / 2;
        PointCount = n;
        Stride = (2 * n) + 1;

        _x = new double[2 * n];
        _y = new double[2 * n];
        for (int t = 0; t < n; t++)
        {
            _x[t] = w[t];
            _x[n + t] = w[t];
            _y[t] = w[n + t];
            _y[n + t] = w[n + t];
        }

        _prefix = new double[Stride * PrefixLanes];
        for (int i = 0; i < 2 * n; i++)
        {
            int from = i * PrefixLanes;
            int to = from + PrefixLanes;
            _prefix[to + 0] = _prefix[from + 0] + _x[i];
            _prefix[to + 1] = _prefix[from + 1] + _y[i];
            _prefix[to + 2] = _prefix[from + 2] + (_x[i] * _x[i]);
            _prefix[to + 3] = _prefix[from + 3] + (_y[i] * _y[i]);
            _prefix[to + 4] = _prefix[from + 4] + (_x[i] * _y[i]);
        }

        _anti = new double[n * Stride * Lanes];
        _diag = new double[n * Stride * Lanes];

        for (int s = 0; s < n; s++)
        {
            int rowBase = s * Stride;
            for (int i = 0; i < 2 * n; i++)
            {
                int anti = Mod(s - i, n);
                int diag = (i + s) % n;

                int from = (rowBase + i) * Lanes;
                int to = from + Lanes;

                _anti[to + 0] = _anti[from + 0] + (_x[i] * _x[anti]);
                _anti[to + 1] = _anti[from + 1] + (_x[i] * _y[anti]);
                _anti[to + 2] = _anti[from + 2] + (_y[i] * _x[anti]);
                _anti[to + 3] = _anti[from + 3] + (_y[i] * _y[anti]);

                _diag[to + 0] = _diag[from + 0] + (_x[i] * _x[diag]);
                _diag[to + 1] = _diag[from + 1] + (_x[i] * _y[diag]);
                _diag[to + 2] = _diag[from + 2] + (_y[i] * _x[diag]);
                _diag[to + 3] = _diag[from + 3] + (_y[i] * _y[diag]);
            }
        }
    }

    /// <summary>The number of goal points, n.</summary>
    public int PointCount { get; }

    /// <summary>The row length of the two dimensional tables, 2n + 1.</summary>
    public int Stride { get; }

    /// <summary>The goal x coordinates, repeated twice.</summary>
    public ReadOnlySpan<double> X => _x;

    /// <summary>The goal y coordinates, repeated twice.</summary>
    public ReadOnlySpan<double> Y => _y;

    /// <summary>The prefix sum of x up to, but not including, an index.</summary>
    /// <param name="m">The index.</param>
    /// <returns>The sum.</returns>
    public double SumX(int m) => _prefix[(m * PrefixLanes) + 0];

    /// <summary>The prefix sum of y.</summary>
    /// <param name="m">The index.</param>
    /// <returns>The sum.</returns>
    public double SumY(int m) => _prefix[(m * PrefixLanes) + 1];

    /// <summary>The prefix sum of x squared.</summary>
    /// <param name="m">The index.</param>
    /// <returns>The sum.</returns>
    public double SumXx(int m) => _prefix[(m * PrefixLanes) + 2];

    /// <summary>The prefix sum of y squared.</summary>
    /// <param name="m">The index.</param>
    /// <returns>The sum.</returns>
    public double SumYy(int m) => _prefix[(m * PrefixLanes) + 3];

    /// <summary>The prefix sum of x times y.</summary>
    /// <param name="m">The index.</param>
    /// <returns>The sum.</returns>
    public double SumXy(int m) => _prefix[(m * PrefixLanes) + 4];

    /// <summary>
    /// One anti-diagonal sum: the total over i below m of f(i) times g of (s minus i),
    /// where the lane picks which of x against x, x against y, y against x or y against y
    /// is wanted.
    /// </summary>
    /// <param name="s">The fixed index sum.</param>
    /// <param name="m">The exclusive upper bound.</param>
    /// <param name="lane">Which of the four products, in the order xx, xy, yx, yy.</param>
    /// <returns>The sum.</returns>
    public double Anti(int s, int m, int lane) => _anti[(((s * Stride) + m) * Lanes) + lane];

    /// <summary>One diagonal sum, where the index difference rather than the sum is fixed.</summary>
    /// <param name="d">The fixed index difference.</param>
    /// <param name="m">The exclusive upper bound.</param>
    /// <param name="lane">Which of the four products, in the order xx, xy, yx, yy.</param>
    /// <returns>The sum.</returns>
    public double Diag(int d, int m, int lane) => _diag[(((d * Stride) + m) * Lanes) + lane];

    /// <summary>The backing arrays, for the hot loop of the fast evaluator (SPEC §0.6).</summary>
    /// <returns>The coordinates, the interleaved prefix sums, and the two interleaved tables.</returns>
    internal (double[] X, double[] Y, double[] Prefix, double[] Anti, double[] Diag) Raw() =>
        (_x, _y, _prefix, _anti, _diag);

    /// <summary>The number of bytes the tables occupy, for the memory budget of SPEC §12.</summary>
    public long ByteCount => ((long)_anti.Length + _diag.Length + _prefix.Length + _x.Length + _y.Length)
        * sizeof(double);

    /// <summary>The non negative remainder.</summary>
    /// <param name="value">The value.</param>
    /// <param name="modulus">The modulus.</param>
    /// <returns>The remainder in the range zero to modulus minus one.</returns>
    internal static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
