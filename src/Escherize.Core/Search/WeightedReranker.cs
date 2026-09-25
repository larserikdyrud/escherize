using Escherize.Parametrization;
using Escherize.Preprocessing;
using Escherize.Templates;

namespace Escherize.Search;

/// <summary>
/// Reranks the raw candidates with a weight per goal point, so that the places the user
/// marked count for more (SPEC §7.4).
/// </summary>
/// <remarks>
/// The weight acts the same on both coordinates of a point, so it commutes with a
/// rotation, and the measures of SPEC §6.1 apply unchanged once the basis has been scaled
/// and made orthonormal again.
/// </remarks>
public static class WeightedReranker
{
    /// <summary>
    /// Scores every raw candidate with the weighted measure, reorders them, and then
    /// applies the filters of SPEC §7.3.
    /// </summary>
    /// <param name="goal">The normalised goal.</param>
    /// <param name="weights">One weight per goal point (SPEC §4.6).</param>
    /// <param name="raw">The raw candidates.</param>
    /// <param name="options">The search options.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The candidates that survived, best weighted first.</returns>
    public static List<Candidate> Rerank(
        GoalShape goal,
        double[] weights,
        IReadOnlyList<ScoredCandidate> raw,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(options);

        var scored = new List<ScoredCandidate>(raw.Count);
        foreach (ScoredCandidate candidate in raw)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double weighted = WeightedError(goal, weights, candidate);
            if (double.IsFinite(weighted))
            {
                scored.Add(new ScoredCandidate(candidate.Key, weighted));
            }
        }

        scored.Sort(CandidateOrder.Instance);

        // The unweighted error is kept for the report, so the two can be compared.
        var unweighted = new Dictionary<(int, int, bool, string), double>();
        foreach (ScoredCandidate candidate in raw)
        {
            unweighted[KeyOf(candidate.Key)] = candidate.Error;
        }

        var kept = CandidatePostProcessor.Process(goal, scored, options, cancellationToken);
        var result = new List<Candidate>(kept.Count);
        foreach (Candidate candidate in kept)
        {
            double plain = unweighted.TryGetValue(KeyOf(candidate.Key), out double value)
                ? value
                : candidate.Error;

            // Process filled Error with the weighted value it ranked by, so the two are
            // put back the right way round here.
            result.Add(Clone(candidate, error: plain, weightedError: candidate.Error));
        }

        return result;
    }

    /// <summary>
    /// The weighted distance of one candidate (SPEC §7.4): the basis is scaled by the
    /// square root of the weights and made orthonormal again, the goal is scaled the same
    /// way, and the measure of SPEC §6.1 is applied to the pair.
    /// </summary>
    /// <param name="goal">The normalised goal.</param>
    /// <param name="weights">One weight per goal point.</param>
    /// <param name="candidate">The candidate.</param>
    /// <returns>The weighted error, or infinity when the basis could not be built.</returns>
    public static double WeightedError(GoalShape goal, double[] weights, ScoredCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(candidate);

        TemplateSpec template = TemplateLibrary.All[candidate.Key.TypeIndex];
        int n = goal.PointCount;

        TileLayout layout;
        TileBasis basis;
        try
        {
            layout = TileLayout.Create(template, candidate.Key.K);
            basis = DenseBasisBuilder.Build(layout);
        }
        catch (InvalidOperationException)
        {
            return double.PositiveInfinity;
        }

        if (layout.PointCount != n)
        {
            return double.PositiveInfinity;
        }

        // The weight of template point t is the weight of the goal point it maps to.
        var root = new double[n];
        ReadOnlySpan<double> source = candidate.Key.Reversed ? goal.WReversed : goal.W;
        var shifted = new double[2 * n];

        for (int t = 0; t < n; t++)
        {
            int index = (t + candidate.Key.J) % n;
            int goalIndex = candidate.Key.Reversed ? (n - index) % n : index;
            root[t] = Math.Sqrt(weights[goalIndex]);
            shifted[t] = source[index] * root[t];
            shifted[n + t] = source[n + index] * root[t];
        }

        // The measures of SPEC §6.1 are stated for a goal of unit norm; scaling the whole
        // problem does not change the ranking, so the weighted goal is normalised too.
        double norm = 0;
        foreach (double value in shifted)
        {
            norm += value * value;
        }

        norm = Math.Sqrt(norm);
        if (norm <= 0)
        {
            return double.PositiveInfinity;
        }

        for (int i = 0; i < shifted.Length; i++)
        {
            shifted[i] /= norm;
        }

        var weightedBasis = new DenseMatrix(2 * n, basis.ColumnCount);
        for (int t = 0; t < n; t++)
        {
            for (int c = 0; c < basis.ColumnCount; c++)
            {
                weightedBasis[t, c] = basis.Matrix[t, c] * root[t];
                weightedBasis[n + t, c] = basis.Matrix[n + t, c] * root[t];
            }
        }

        try
        {
            NullspaceSolver.Orthonormalize(weightedBasis);
        }
        catch (InvalidOperationException)
        {
            return double.PositiveInfinity;
        }

        var scaled = new TileBasis(layout, weightedBasis, basis.VertexColumnCount);
        return TileFit.Fit(scaled, shifted).Error;
    }

    /// <summary>A comparable form of a candidate key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The tuple used for lookup.</returns>
    private static (int, int, bool, string) KeyOf(CandidateKey key) =>
        (key.TypeIndex, key.J, key.Reversed, string.Join(",", key.K));

    /// <summary>Copies a candidate with the two error fields set.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="error">The unweighted error.</param>
    /// <param name="weightedError">The weighted error.</param>
    /// <returns>The copy.</returns>
    private static Candidate Clone(Candidate candidate, double error, double weightedError) => new()
    {
        Key = candidate.Key,
        Template = candidate.Template,
        Layout = candidate.Layout,
        Error = error,
        WeightedError = weightedError,
        NormalizedTile = candidate.NormalizedTile,
        Tile = candidate.Tile,
        GoalOrderedTile = candidate.GoalOrderedTile,
        VertexIndices = candidate.VertexIndices,
        Isometries = candidate.Isometries,
        OverlayRotation = candidate.OverlayRotation,
        RelativeNeckWidth = candidate.RelativeNeckWidth,
    };
}
