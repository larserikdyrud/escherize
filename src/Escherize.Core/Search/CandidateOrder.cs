namespace Escherize.Search;

/// <summary>
/// The total order on candidates of SPEC §7.1: by error, then template index, then k
/// lexicographically, then start offset, then orientation. Being total, it makes the
/// result independent of how the work was split over threads.
/// </summary>
public sealed class CandidateOrder : IComparer<ScoredCandidate>
{
    /// <summary>
    /// The step the error is rounded to before candidates are compared, matching the
    /// margin of SPEC §7.2.
    /// </summary>
    /// <remarks>
    /// Two candidates can have the same error for a real reason, typically because the
    /// goal is symmetric, and then the key decides which comes first. Comparing the raw
    /// doubles would instead let the last few bits decide, and those depend on the order
    /// the sums happened to be accumulated in. Rounding first keeps the comparison a total
    /// order, since it is a plain function of the error, and makes the ranking depend on
    /// the specified key rather than on arithmetic noise.
    /// </remarks>
    public const double ErrorQuantum = 1e-12;

    /// <summary>The shared instance.</summary>
    public static CandidateOrder Instance { get; } = new();

    /// <summary>Rounds an error to the comparison grid.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The rounded error.</returns>
    public static double Quantize(double error) => Math.Round(error / ErrorQuantum) * ErrorQuantum;

    /// <inheritdoc/>
    public int Compare(ScoredCandidate? x, ScoredCandidate? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int byError = Quantize(x.Error).CompareTo(Quantize(y.Error));
        if (byError != 0)
        {
            return byError;
        }

        int byType = x.Key.TypeIndex.CompareTo(y.Key.TypeIndex);
        if (byType != 0)
        {
            return byType;
        }

        int byK = CompareK(x.Key.K, y.Key.K);
        if (byK != 0)
        {
            return byK;
        }

        int byJ = x.Key.J.CompareTo(y.Key.J);
        return byJ != 0 ? byJ : x.Key.Reversed.CompareTo(y.Key.Reversed);
    }

    /// <summary>Lexicographic comparison of two k vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>A negative value when the first sorts first.</returns>
    public static int CompareK(int[] left, int[] right)
    {
        int shared = Math.Min(left.Length, right.Length);
        for (int i = 0; i < shared; i++)
        {
            if (left[i] != right[i])
            {
                return left[i].CompareTo(right[i]);
            }
        }

        return left.Length.CompareTo(right.Length);
    }
}

/// <summary>
/// Keeps the best candidates seen so far, in the order of <see cref="CandidateOrder"/>.
/// Each worker fills its own collector and the collectors are merged at the end
/// (SPEC §7.1).
/// </summary>
/// <param name="capacity">How many candidates to keep.</param>
public sealed class TopCandidates(int capacity)
{
    private readonly List<ScoredCandidate> _items = new(2 * capacity);
    private bool _sorted = true;
    private double _threshold = double.PositiveInfinity;

    /// <summary>How many candidates are kept.</summary>
    public int Capacity { get; } = capacity;

    /// <summary>The number of candidates currently held.</summary>
    public int Count => _items.Count;

    /// <summary>
    /// The error a candidate has to beat to be worth keeping. It is infinite until the
    /// collector has filled up once.
    /// </summary>
    /// <remarks>
    /// The search offers one candidate per evaluation, which runs into the tens of
    /// millions, while only a couple of hundred are ever kept. Testing the error against
    /// this bound first means the overwhelming majority cost one comparison rather than an
    /// allocation and a share of a sort.
    /// </remarks>
    public double Threshold => _threshold;

    /// <summary>
    /// Whether a candidate with this error could still be kept. The caller can use it to
    /// avoid building a candidate that would be dropped immediately.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>True when the candidate is worth offering.</returns>
    public bool IsWorthOffering(double error) => error <= _threshold;

    /// <summary>Offers a candidate; it is kept only if it is among the best.</summary>
    /// <param name="candidate">The candidate.</param>
    public void Offer(ScoredCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Error > _threshold)
        {
            return;
        }

        _items.Add(candidate);
        _sorted = false;
        if (_items.Count >= 2 * Capacity)
        {
            Trim();
        }
    }

    /// <summary>Merges another collector into this one.</summary>
    /// <param name="other">The collector to absorb.</param>
    public void Merge(TopCandidates other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (ScoredCandidate candidate in other._items)
        {
            Offer(candidate);
        }
    }

    /// <summary>The kept candidates, best first.</summary>
    /// <returns>The sorted candidates.</returns>
    public List<ScoredCandidate> ToSortedList()
    {
        Trim();
        return [.. _items];
    }

    /// <summary>Sorts, truncates to the capacity, and updates the threshold.</summary>
    private void Trim()
    {
        if (!_sorted)
        {
            _items.Sort(CandidateOrder.Instance);
            _sorted = true;
        }

        if (_items.Count > Capacity)
        {
            _items.RemoveRange(Capacity, _items.Count - Capacity);
        }

        // Once the collector is full, anything worse than its last entry is of no use.
        // Candidates that tie with it on the quantised error are still let through, so
        // that the key of SPEC §7.1 decides between them rather than the arrival order.
        if (_items.Count >= Capacity)
        {
            _threshold = _items[^1].Error;
        }
    }
}
