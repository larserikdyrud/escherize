using System.Globalization;
using Escherize.Geometry;
using Escherize.Parametrization;

namespace Escherize.Templates;

/// <summary>The outcome of validating one template configuration (SPEC §8.2).</summary>
/// <param name="Passed">Whether every check succeeded.</param>
/// <param name="Reason">An empty string on success, otherwise what went wrong.</param>
/// <param name="TileCount">The number of tiles in the patch that was tested.</param>
/// <param name="WorstEdgeError">The largest edge coverage error that was seen.</param>
public sealed record TemplateValidationResult(bool Passed, string Reason, int TileCount, double WorstEdgeError)
{
    /// <summary>A failure with the given reason.</summary>
    /// <param name="reason">What went wrong.</param>
    /// <returns>The result.</returns>
    public static TemplateValidationResult Fail(string reason) => new(false, reason, 0, double.NaN);
}

/// <summary>
/// Checks that a template configuration really produces tiles that tile the plane
/// (SPEC §8.2). This is the test that decides the glide axes and the sign of the rotation
/// angles, which SPEC §0.4 deliberately leaves open.
/// </summary>
public static class TemplateValidator
{
    /// <summary>The tolerance of the edge coverage check (SPEC §8.2 step 3).</summary>
    public const double EdgeCoverageTolerance = 1e-9;

    /// <summary>
    /// The depth of the breadth first search that grows the patch (SPEC §8.2 step 4).
    /// </summary>
    /// <remarks>
    /// SPEC §8.2 says depth 3, which is enough for the hexagonal templates but not for
    /// IH21: around its six fold rotation centre a patch of depth 3 stops short of the
    /// sampling disc of radius 2 R, and the check then reports a gap that the tiling does
    /// not actually have. Growing the patch only adds tiles that must not overlap and
    /// closes gaps that are artefacts of the patch, so a larger depth makes the check
    /// stricter rather than weaker. The sampling disc stays at the radius the
    /// specification gives.
    /// </remarks>
    public const int PatchDepth = 5;

    /// <summary>The number of sample points used for the covering check (SPEC §8.2 step 4).</summary>
    public const int SampleCount = 20_000;

    /// <summary>The relative distance from a tile boundary within which a sample is ignored.</summary>
    public const double BoundaryToleranceFraction = 1e-6;

    /// <summary>The number of attempts made to draw a tile that does not cross itself.</summary>
    public const int MaximumAttempts = 50;

    /// <summary>The relative size of the random perturbation applied to the fitted tile.</summary>
    public const double PerturbationScale = 0.05;

    /// <summary>The relative amplitude of the noise on the goal circle (SPEC §8.2 step 1).</summary>
    public const double GoalNoise = 0.15;

    /// <summary>
    /// Validates one configuration against one seeded goal shape, following the five steps
    /// of SPEC §8.2.
    /// </summary>
    /// <param name="template">The configured template.</param>
    /// <param name="goalPointCount">The number of goal points, n.</param>
    /// <param name="seed">The random seed, which makes the test reproducible.</param>
    /// <returns>The result.</returns>
    public static TemplateValidationResult Validate(TemplateSpec template, int goalPointCount, int seed)
    {
        ArgumentNullException.ThrowIfNull(template);

        int[][] candidates = [.. KVectorEnumerator.Enumerate(template, goalPointCount, minimumK: 2)];
        if (candidates.Length == 0)
        {
            return TemplateValidationResult.Fail(
                $"No k vector with every k at least 2 exists for n = {goalPointCount}.");
        }

        // A draw is a k vector, a goal and a noise vector together (SPEC §8.2 steps 1 and
        // 2); on a self intersecting outline the whole draw is repeated. Every draw is
        // seeded from the caller's seed, so the result stays reproducible.
        Vec2[]? tile = null;
        TileLayout? layout = null;

        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var random = new Random(HashCode.Combine(seed, attempt));
            int[] k = candidates[random.Next(candidates.Length)];

            TileBasis basis;
            try
            {
                layout = TileLayout.Create(template, k);
                basis = DenseBasisBuilder.Build(layout);
            }
            catch (InvalidOperationException exception)
            {
                return TemplateValidationResult.Fail($"The basis could not be built: {exception.Message}");
            }

            // u = B (B^T w) + 0.05 B r, where the fit uses the measure the template calls
            // for (SPEC §6.1). A glide template is fitted with the rotation free; using the
            // plain projection there would align the goal to the fixed glide axes by
            // accident and fold most of the draws.
            double[] w = NoisyCircle(goalPointCount, HashCode.Combine(seed, attempt, 31));
            double[] xi = TileFit.Fit(basis, w).Xi;

            // r is drawn from the standard normal and then scaled to unit length, so that
            // the perturbation really is the five per cent of SPEC §8.2 step 2. Leaving it
            // unscaled would make the nudge grow with the number of columns, about 32 per
            // cent at n = 36, which folds the outline in roughly nine draws out of ten.
            var r = new double[basis.ColumnCount];
            double norm = 0;
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = NextGaussian(random);
                norm += r[i] * r[i];
            }

            norm = Math.Sqrt(norm);
            var perturbation = new double[basis.ColumnCount];
            for (int i = 0; i < perturbation.Length; i++)
            {
                perturbation[i] = xi[i] + (PerturbationScale * r[i] / norm);
            }

            // SPEC §8.2 step 2 redraws on self intersection only. The orientation is not a
            // criterion here: a glide template naturally produces a clockwise outline, and
            // the search covers that by running on both W and W_rev (SPEC §4.5).
            Vec2[] candidate = basis.ToTile(perturbation);
            if (!PolygonQuality.SelfIntersects(candidate))
            {
                tile = candidate;
                break;
            }
        }

        if (tile is null || layout is null)
        {
            return TemplateValidationResult.Fail(
                $"No simple tile was produced in {MaximumAttempts} attempts.");
        }

        double worstEdgeError = CheckEdgeCoverage(layout, tile);
        if (!(worstEdgeError <= EdgeCoverageTolerance))
        {
            return new TemplateValidationResult(
                false,
                string.Create(CultureInfo.InvariantCulture, $"Edge coverage is off by {worstEdgeError:0.###e+00}."),
                0,
                worstEdgeError);
        }

        return CheckNeighbourhood(layout, tile, TileIsometries.Build(layout, tile), seed, worstEdgeError);
    }

    /// <summary>
    /// Checks that the neighbour isometry of every edge maps the partner edge onto that
    /// edge, point for point (SPEC §8.2 step 3).
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="tile">The tile.</param>
    /// <param name="isometries">
    /// The neighbour isometries of this tile, or null to build them from the outline. A
    /// tile that has been turned, as a search result is, must pass its own: a glide
    /// relation is stated against a fixed axis, so rebuilding it from turned points gives
    /// the wrong map.
    /// </param>
    /// <returns>The largest deviation.</returns>
    public static double CheckEdgeCoverage(TileLayout layout, Vec2[] tile, Isometry[]? isometries = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(tile);

        isometries ??= TileIsometries.Build(layout, tile);
        int[] partners = TileIsometries.PartnerEdges(layout.Template);
        double worst = 0;

        for (int edge = 0; edge < partners.Length; edge++)
        {
            int partner = partners[edge];
            int k = layout.EdgeK(edge);

            // A glide pair relates its points directly, index for index, because the
            // relation of SPEC §5.1 is b(i) - b(0) = F (a(i) - a(0)). The C, T and R
            // relations all run their indices in opposite directions, so there the image
            // of the partner edge is this edge traversed backwards.
            bool reversed = layout.Template.Edges[edge].Kind != EdgeKind.G;

            for (int i = 0; i <= k + 1; i++)
            {
                Vec2 image = isometries[edge].Apply(tile[layout.EdgePointIndex(partner, i)]);
                Vec2 expected = tile[layout.EdgePointIndex(edge, reversed ? k + 1 - i : i)];
                worst = Math.Max(worst, image.DistanceTo(expected));
            }
        }

        return worst;
    }

    /// <summary>
    /// Checks a tile that already exists, rather than generating one. This is what proves
    /// that a particular search result really tiles the plane, which matters before it is
    /// committed to material.
    /// </summary>
    /// <param name="layout">The layout the tile belongs to.</param>
    /// <param name="tile">The tile points, in boundary order.</param>
    /// <param name="isometries">The neighbour isometries belonging to this tile.</param>
    /// <param name="seed">The random seed for the sample points.</param>
    /// <returns>The result, covering both the edge coverage and the covering test.</returns>
    public static TemplateValidationResult ValidateTile(
        TileLayout layout,
        Vec2[] tile,
        Isometry[] isometries,
        int seed = 1)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(isometries);

        if (PolygonQuality.SelfIntersects(tile))
        {
            return TemplateValidationResult.Fail("The tile outline crosses itself.");
        }

        double worstEdgeError = CheckEdgeCoverage(layout, tile, isometries);
        if (!(worstEdgeError <= EdgeCoverageTolerance))
        {
            return new TemplateValidationResult(
                false,
                string.Create(CultureInfo.InvariantCulture, $"Edge coverage is off by {worstEdgeError:0.###e+00}."),
                0,
                worstEdgeError);
        }

        return CheckNeighbourhood(layout, tile, isometries, seed, worstEdgeError);
    }

    /// <summary>
    /// Grows a patch and checks that it is a tiling: no two tiles overlap, and every edge
    /// of an interior tile is shared with exactly one neighbour.
    /// </summary>
    /// <remarks>
    /// SPEC §8.2 samples a disc of twice the tile radius and asks for exactly one covering
    /// tile. That suits the roughly round tiles validation generates, but not a tile fitted
    /// to a long thin goal: its radius is half its length, while the patch grows along its
    /// axis, so the disc reaches past the patch and every point out there looks like a hole.
    /// The two halves of the question are therefore asked separately, and neither depends
    /// on how far the patch happens to reach. An overlap is found by sampling, and cannot
    /// be an artefact: if two tiles cover one point, the tiling is broken. A gap is found
    /// by counting edges instead, on the argument that closes a surface: if every edge of
    /// every tile that has its full neighbourhood is shared with exactly one other tile,
    /// there is nowhere for a gap to be.
    /// </remarks>
    /// <param name="layout">The layout.</param>
    /// <param name="tile">The tile.</param>
    /// <param name="isometries">The neighbour isometries of this tile.</param>
    /// <param name="seed">The random seed for the sample points.</param>
    /// <param name="worstEdgeError">The edge coverage error, carried into the result.</param>
    /// <returns>The result.</returns>
    private static TemplateValidationResult CheckNeighbourhood(
        TileLayout layout,
        Vec2[] tile,
        Isometry[] isometries,
        int seed,
        double worstEdgeError)
    {
        List<PlacedTile> patch = TilingPatch.Grow(tile, isometries, PatchDepth);
        if (patch.Count < 2)
        {
            return new TemplateValidationResult(false, "The patch has no neighbours.", patch.Count, worstEdgeError);
        }

        double diameter = PolygonOps.Diameter(tile);
        double tolerance = BoundaryToleranceFraction * diameter;
        double quantum = 1e-6 * diameter;

        // Every tiling edge of every tile, keyed by its two endpoints so that the two
        // tiles meeting along it produce the same key.
        var uses = new Dictionary<(long, long, long, long), int>();
        foreach (PlacedTile placed in patch)
        {
            for (int s = 0; s < layout.VertexCount; s++)
            {
                Vec2 a = placed.Points[layout.EdgePointIndex(s, 0)];
                Vec2 b = placed.Points[layout.EdgePointIndex(s, layout.EdgeK(s) + 1)];
                (long, long, long, long) key = EdgeKey(a, b, quantum);
                uses[key] = uses.GetValueOrDefault(key) + 1;
            }
        }

        // A tile whose every edge is shared has its full neighbourhood inside the patch;
        // those are the ones that can say anything about gaps.
        int interior = 0;
        foreach (PlacedTile placed in patch)
        {
            bool complete = true;
            for (int s = 0; s < layout.VertexCount && complete; s++)
            {
                Vec2 a = placed.Points[layout.EdgePointIndex(s, 0)];
                Vec2 b = placed.Points[layout.EdgePointIndex(s, layout.EdgeK(s) + 1)];
                int count = uses[EdgeKey(a, b, quantum)];

                if (count > 2)
                {
                    return new TemplateValidationResult(
                        false,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"An edge is shared by {count} tiles, so the tiling folds onto itself."),
                        patch.Count,
                        worstEdgeError);
                }

                complete = count == 2;
            }

            if (complete)
            {
                interior++;
            }
        }

        if (interior == 0)
        {
            return new TemplateValidationResult(
                false,
                "No tile in the patch has a complete ring of neighbours.",
                patch.Count,
                worstEdgeError);
        }

        // Overlaps: sampled over the base tile and the ring around it. Two tiles covering
        // one point is a fault whatever the patch looks like.
        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;
        foreach (Vec2 point in tile)
        {
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        }

        double marginX = 0.5 * (maxX - minX);
        double marginY = 0.5 * (maxY - minY);
        minX -= marginX;
        maxX += marginX;
        minY -= marginY;
        maxY += marginY;

        var random = new Random(seed + 7919);
        for (int sample = 0; sample < SampleCount; sample++)
        {
            var point = new Vec2(
                minX + (random.NextDouble() * (maxX - minX)),
                minY + (random.NextDouble() * (maxY - minY)));

            int covering = 0;
            bool nearBoundary = false;
            foreach (PlacedTile placed in patch)
            {
                if (!placed.BoxContains(point, tolerance))
                {
                    continue;
                }

                if (PolygonQuality.DistanceToBoundary(placed.Points, point) <= tolerance)
                {
                    nearBoundary = true;
                    break;
                }

                if (PolygonQuality.Contains(placed.Points, point))
                {
                    covering++;
                }
            }

            if (!nearBoundary && covering > 1)
            {
                return new TemplateValidationResult(
                    false,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Sample {sample} at ({point.X:0.####}, {point.Y:0.####}) lies in {covering} tiles."),
                    patch.Count,
                    worstEdgeError);
            }
        }

        return new TemplateValidationResult(true, string.Empty, interior, worstEdgeError);
    }

    /// <summary>
    /// A key for one tiling edge, the same from either of the two tiles that meet along it.
    /// </summary>
    /// <param name="a">One endpoint.</param>
    /// <param name="b">The other endpoint.</param>
    /// <param name="quantum">The rounding step.</param>
    /// <returns>The key.</returns>
    private static (long, long, long, long) EdgeKey(Vec2 a, Vec2 b, double quantum)
    {
        long ax = (long)Math.Round(a.X / quantum);
        long ay = (long)Math.Round(a.Y / quantum);
        long bx = (long)Math.Round(b.X / quantum);
        long by = (long)Math.Round(b.Y / quantum);

        bool firstIsLower = ax < bx || (ax == bx && ay <= by);
        return firstIsLower ? (ax, ay, bx, by) : (bx, by, ax, ay);
    }

    /// <summary>
    /// A circle of the given size with seeded noise, normalised to zero mean and unit
    /// coordinate norm, used as the goal in the validation (SPEC §8.2 step 1).
    /// </summary>
    /// <param name="pointCount">The number of points.</param>
    /// <param name="seed">The random seed.</param>
    /// <param name="noise">The relative amplitude of the radial noise.</param>
    /// <returns>The coordinate vector of length 2n.</returns>
    public static double[] NoisyCircle(int pointCount, int seed, double noise = GoalNoise)
    {
        var random = new Random(seed);
        var points = new Vec2[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            double angle = 2 * Math.PI * i / pointCount;
            double radius = 1.0 + (noise * ((2 * random.NextDouble()) - 1));
            points[i] = new Vec2(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }

        Vec2 centroid = PolygonOps.PointAverage(points);
        double norm = 0;
        for (int i = 0; i < pointCount; i++)
        {
            points[i] -= centroid;
            norm += points[i].LengthSquared;
        }

        norm = Math.Sqrt(norm);
        var w = new double[2 * pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            w[i] = points[i].X / norm;
            w[pointCount + i] = points[i].Y / norm;
        }

        return w;
    }

    /// <summary>A standard normal sample, by the polar form of the Box-Muller transform.</summary>
    /// <param name="random">The generator.</param>
    /// <returns>The sample.</returns>
    private static double NextGaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
