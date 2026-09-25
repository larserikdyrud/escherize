using Escherize.Geometry;
using Escherize.Parametrization;
using Escherize.Preprocessing;
using Escherize.Templates;

namespace Escherize.Search;

/// <summary>
/// Turns raw keys into tiles and applies the filters of SPEC §7.3, in order:
/// reconstruction, self intersection, neck width, diversity, then the best K.
/// </summary>
public static class CandidatePostProcessor
{
    /// <summary>
    /// The margin within which two reconstructed tiles count as the same tile, whatever
    /// the diversity threshold is (SPEC §7.2).
    /// </summary>
    public const double IdenticalTileTolerance = 1e-12;

    /// <summary>Reconstructs and filters the raw candidates (SPEC §7.3).</summary>
    /// <param name="goal">The normalised goal.</param>
    /// <param name="raw">The raw candidates, best first.</param>
    /// <param name="options">The search options.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The candidates that survived, best first, at most <c>TopK</c> of them.</returns>
    public static List<Candidate> Process(
        GoalShape goal,
        IReadOnlyList<ScoredCandidate> raw,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(options);

        var kept = new List<Candidate>(options.TopK);

        foreach (ScoredCandidate scored in raw)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (kept.Count >= options.TopK)
            {
                break;
            }

            Candidate? candidate = Reconstruct(goal, scored);
            if (candidate is null)
            {
                continue;
            }

            // Step 2: a tile that crosses itself or is turned inside out is unusable.
            if (!PolygonQuality.IsValidTile(candidate.NormalizedTile))
            {
                continue;
            }

            // Step 3: a tile with too thin a neck cannot be produced physically.
            if (options.MinimumNeckWidth > 0 && candidate.RelativeNeckWidth < options.MinimumNeckWidth)
            {
                continue;
            }

            // Step 4: drop anything that is essentially a tile already chosen.
            if (IsDuplicate(candidate, kept, options.Diversity))
            {
                continue;
            }

            kept.Add(candidate);
        }

        return kept;
    }

    /// <summary>
    /// Reconstructs one candidate (SPEC §6.4): the optimal parameters, the tile, the
    /// overlay rotation, the neighbour isometries and the transform back to input units.
    /// </summary>
    /// <param name="goal">The normalised goal.</param>
    /// <param name="scored">The raw candidate.</param>
    /// <returns>The candidate, or null when its basis could not be built.</returns>
    public static Candidate? Reconstruct(GoalShape goal, ScoredCandidate scored)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(scored);

        TemplateSpec template = TemplateLibrary.All[scored.Key.TypeIndex];
        int n = goal.PointCount;

        TileLayout layout;
        TileBasis basis;
        try
        {
            layout = TileLayout.Create(template, scored.Key.K);
            basis = DenseBasisBuilder.Build(layout);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (layout.PointCount != n)
        {
            return null;
        }

        ReadOnlySpan<double> source = scored.Key.Reversed ? goal.WReversed : goal.W;
        var shifted = new double[2 * n];
        for (int t = 0; t < n; t++)
        {
            int index = (t + scored.Key.J) % n;
            shifted[t] = source[index];
            shifted[n + t] = source[n + index];
        }

        TileFitResult fit = TileFit.Fit(basis, shifted);
        Vec2[] tile = basis.ToTile(fit.Xi);

        // The optimal overlay rotation of SPEC §6.4.
        double cross = 0;
        double dot = 0;
        for (int t = 0; t < n; t++)
        {
            double xu = tile[t].X;
            double yu = tile[t].Y;
            double xw = shifted[t];
            double yw = shifted[n + t];
            cross += (xu * yw) - (yu * xw);
            dot += (xu * xw) + (yu * yw);
        }

        double theta = Math.Atan2(cross, dot);

        // The tile is turned into the frame of the goal so that the drawings and the
        // exported outline line up with the silhouette. The neighbour isometries are
        // conjugated by the same rotation, which keeps the patch consistent: a glide
        // relation is stated against a fixed axis, so the isometries cannot simply be
        // rebuilt from the rotated points.
        Isometry[] isometries = TileIsometries.Build(layout, tile);
        var rotation = Isometry.Rotation(Vec2.Zero, theta);
        Isometry inverseRotation = rotation.Inverse();
        for (int e = 0; e < isometries.Length; e++)
        {
            isometries[e] = inverseRotation.Then(isometries[e]).Then(rotation);
        }

        var normalized = new Vec2[n];
        for (int t = 0; t < n; t++)
        {
            normalized[t] = tile[t].Rotate(theta);
        }

        var denormalized = new Vec2[n];
        var goalOrdered = new Vec2[n];
        for (int t = 0; t < n; t++)
        {
            denormalized[t] = goal.Denormalize(normalized[t]);
        }

        var vertexIndices = new int[layout.VertexCount];
        for (int s = 0; s < vertexIndices.Length; s++)
        {
            vertexIndices[s] = layout.VertexIndex(s);
        }

        var candidate = new Candidate
        {
            Key = scored.Key,
            Template = template,
            Layout = layout,
            Error = scored.Error,
            NormalizedTile = normalized,
            Tile = denormalized,
            GoalOrderedTile = goalOrdered,
            VertexIndices = vertexIndices,
            Isometries = isometries,
            OverlayRotation = theta,
            RelativeNeckWidth = PolygonQuality.RelativeNeckWidth(normalized),
        };

        // The diversity filter compares tiles point by point in goal index order, so that
        // candidates from different templates and offsets line up (SPEC §7.3).
        for (int t = 0; t < n; t++)
        {
            goalOrdered[candidate.GoalIndex(t)] = normalized[t];
        }

        return candidate;
    }

    /// <summary>
    /// Whether a candidate is within the diversity threshold of one already chosen
    /// (SPEC §7.3 step 4).
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="chosen">The candidates already kept.</param>
    /// <param name="threshold">The Procrustes distance below which they count as the same.</param>
    /// <returns>True when the candidate should be dropped.</returns>
    private static bool IsDuplicate(Candidate candidate, List<Candidate> chosen, double threshold)
    {
        // A candidate whose reconstructed tile is identical to one already chosen is a
        // duplicate whatever the threshold is (SPEC §7.2); the threshold only widens that
        // to tiles that are merely similar.
        double effective = Math.Max(threshold, IdenticalTileTolerance);

        foreach (Candidate other in chosen)
        {
            if (ProcrustesDistance(candidate.GoalOrderedTile, other.GoalOrderedTile) < effective)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The full Procrustes distance between two point sets, with the rotation and the
    /// scale free (SPEC §7.3). Both sets are centred and scaled to unit norm first, so the
    /// result lies between 0 and 1.
    /// </summary>
    /// <param name="left">The first point set.</param>
    /// <param name="right">The second point set, of the same length.</param>
    /// <returns>The distance.</returns>
    public static double ProcrustesDistance(ReadOnlySpan<Vec2> left, ReadOnlySpan<Vec2> right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            return double.PositiveInfinity;
        }

        Vec2 leftCentre = PolygonOps.PointAverage(left);
        Vec2 rightCentre = PolygonOps.PointAverage(right);

        double leftNorm = 0;
        double rightNorm = 0;
        for (int i = 0; i < left.Length; i++)
        {
            leftNorm += (left[i] - leftCentre).LengthSquared;
            rightNorm += (right[i] - rightCentre).LengthSquared;
        }

        leftNorm = Math.Sqrt(leftNorm);
        rightNorm = Math.Sqrt(rightNorm);
        if (leftNorm <= 0 || rightNorm <= 0)
        {
            return double.PositiveInfinity;
        }

        // In the plane the best rotation follows in closed form: treating the points as
        // complex numbers, the correlation is the magnitude of the sum of a conjugate
        // times b.
        double real = 0;
        double imaginary = 0;
        for (int i = 0; i < left.Length; i++)
        {
            Vec2 a = (left[i] - leftCentre) / leftNorm;
            Vec2 b = (right[i] - rightCentre) / rightNorm;
            real += (a.X * b.X) + (a.Y * b.Y);
            imaginary += (a.X * b.Y) - (a.Y * b.X);
        }

        double correlation = Math.Sqrt((real * real) + (imaginary * imaginary));
        return Math.Sqrt(Math.Max(0, 1 - (correlation * correlation)));
    }
}
