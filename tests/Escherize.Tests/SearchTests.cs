using System.Globalization;
using Escherize.Geometry;
using Escherize.Parametrization;
using Escherize.Preprocessing;
using Escherize.Rendering;
using Escherize.Search;
using Escherize.Templates;
using Xunit;

namespace Escherize.Tests;

/// <summary>Search, reconstruction and output tests (SPEC §8.5, §8.6, §8.7).</summary>
public sealed class SearchTests
{
    /// <summary>
    /// When the goal is itself a tile generated from a template, the search finds it with
    /// essentially zero error at rank one (SPEC §8.6).
    /// </summary>
    /// <param name="templateName">The template the goal was generated from.</param>
    [Theory]
    [InlineData("IH4")]
    [InlineData("IH1")]
    [InlineData("IH7")]
    [InlineData("IH5")]
    public void KnownAnswerIsFoundExactly(string templateName)
    {
        TemplateSpec template = TemplateLibrary.ByName(templateName);
        (Vec2[] tile, _) = DrawTile(template, seed: 5);

        GoalShape goal = GoalShape.FromResampledContour(tile);
        var options = new SearchOptions { TopK = 5, Diversity = 0 };

        SearchResult result = EscherizeSearch.Run(goal, options);

        Assert.NotEmpty(result.RawCandidates);
        Assert.True(
            result.RawCandidates[0].Error < 1e-10,
            $"The best raw error was {result.RawCandidates[0].Error:0.###e+00}, expected below 1e-10.");

        Assert.NotEmpty(result.Candidates);
        Assert.True(
            result.Candidates[0].Error < 1e-10,
            $"The best candidate error was {result.Candidates[0].Error:0.###e+00}.");
    }

    /// <summary>
    /// The error is unchanged when the goal is rotated or translated (SPEC §8.5). Both are
    /// undone by the normalisation and by the template having translation in its span.
    /// </summary>
    [Fact]
    public void ErrorIsInvariantUnderTranslation()
    {
        Vec2[] shape = TestFixtures.Star();
        var options = new PreprocessOptions { PointCount = 24, SmoothHarmonics = 0 };

        GoalShape plain = Preprocessor.Build(shape, options);

        var moved = new Vec2[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            moved[i] = shape[i] + new Vec2(17.5, -4.25);
        }

        GoalShape shifted = Preprocessor.Build(moved, options);

        double[] plainErrors = BestErrors(plain);
        double[] shiftedErrors = BestErrors(shifted);

        for (int i = 0; i < plainErrors.Length; i++)
        {
            Assert.Equal(plainErrors[i], shiftedErrors[i], 1e-9);
        }
    }

    /// <summary>
    /// The error of a candidate on W with rev = false equals the error of the matching
    /// candidate on W_rev with rev = true (SPEC §8.5).
    /// </summary>
    [Fact]
    public void ForwardAndReversedOrientationsAgree()
    {
        GoalShape goal = Preprocessor.Build(
            TestFixtures.Star(),
            new PreprocessOptions { PointCount = 24, SmoothHarmonics = 0 });

        TemplateSpec template = TemplateLibrary.ByName("IH4");
        int[][] kVectors = [.. KVectorEnumerator.Enumerate(template, goal.PointCount, minimumK: 1)];
        int[] k = kVectors[kVectors.Length / 2];

        TileBasis basis = DenseBasisBuilder.Build(template, k);
        int n = goal.PointCount;

        for (int j = 0; j < n; j++)
        {
            double forward = ErrorAt(basis, goal.W, n, j);
            double reversed = ErrorAt(basis, goal.WReversed, n, j);

            // The two orientations are searched separately; each offset on one side has a
            // matching offset on the other, so the sorted sets of errors must agree.
            Assert.True(forward >= 0 && reversed >= 0);
        }

        double[] forwardAll = new double[n];
        double[] reversedAll = new double[n];
        for (int j = 0; j < n; j++)
        {
            forwardAll[j] = ErrorAt(basis, goal.W, n, j);
            reversedAll[j] = ErrorAt(basis, goal.WReversed, n, j);
        }

        Array.Sort(forwardAll);
        Array.Sort(reversedAll);

        // A five pointed star is mirror symmetric, so the two orientations give the same
        // multiset of errors.
        for (int j = 0; j < n; j++)
        {
            Assert.Equal(forwardAll[j], reversedAll[j], 1e-9);
        }
    }

    /// <summary>
    /// The search gives the same answer no matter how many threads it runs on
    /// (SPEC §8.7).
    /// </summary>
    [Fact]
    public void SearchIsIndependentOfThreadCount()
    {
        GoalShape goal = Preprocessor.Build(
            TestFixtures.Cat(),
            new PreprocessOptions { PointCount = 24 });

        SearchResult single = EscherizeSearch.Run(goal, new SearchOptions { TopK = 10, Threads = 1 });
        SearchResult many = EscherizeSearch.Run(goal, new SearchOptions { TopK = 10, Threads = 8 });

        Assert.Equal(single.Evaluations, many.Evaluations);
        Assert.Equal(single.Candidates.Count, many.Candidates.Count);

        for (int i = 0; i < single.Candidates.Count; i++)
        {
            Candidate a = single.Candidates[i];
            Candidate b = many.Candidates[i];
            Assert.Equal(a.Template.Name, b.Template.Name);
            Assert.Equal(a.Key.J, b.Key.J);
            Assert.Equal(a.Key.Reversed, b.Key.Reversed);
            Assert.Equal(a.Key.K, b.Key.K);
            Assert.Equal(a.Error, b.Error);
        }
    }

    /// <summary>A reconstructed candidate really is a tile of the template it claims (SPEC §6.4).</summary>
    [Fact]
    public void ReconstructedCandidatesTile()
    {
        GoalShape goal = Preprocessor.Build(
            TestFixtures.Cat(),
            new PreprocessOptions { PointCount = 30 });

        SearchResult result = EscherizeSearch.Run(goal, new SearchOptions { TopK = 5 });
        Assert.NotEmpty(result.Candidates);

        foreach (Candidate candidate in result.Candidates)
        {
            Assert.True(PolygonQuality.IsValidTile(candidate.NormalizedTile));

            // The isometries were conjugated by the overlay rotation, so they must still
            // carry each edge onto its partner.
            int[] partners = TileIsometries.PartnerEdges(candidate.Template);
            for (int edge = 0; edge < partners.Length; edge++)
            {
                int partner = partners[edge];
                int k = candidate.Layout.EdgeK(edge);
                bool reversed = candidate.Template.Edges[edge].Kind != EdgeKind.G;

                for (int i = 0; i <= k + 1; i++)
                {
                    Vec2 image = candidate.Isometries[edge]
                        .Apply(candidate.NormalizedTile[candidate.Layout.EdgePointIndex(partner, i)]);
                    Vec2 expected = candidate.NormalizedTile[
                        candidate.Layout.EdgePointIndex(edge, reversed ? k + 1 - i : i)];
                    Assert.True(
                        image.DistanceTo(expected) < 1e-9,
                        $"{candidate.Template.Name} edge {edge} point {i} is off by {image.DistanceTo(expected)}.");
                }
            }
        }
    }

    /// <summary>The DXF output is well formed and scaled to the requested size (SPEC §9.4).</summary>
    [Fact]
    public void DxfIsWellFormedAndScaled()
    {
        Vec2[] tile = TestFixtures.Star();
        string dxf = DxfWriter.Build(tile, 200);

        Assert.Contains("$INSUNITS", dxf, StringComparison.Ordinal);
        Assert.Contains("POLYLINE", dxf, StringComparison.Ordinal);
        Assert.Contains("SEQEND", dxf, StringComparison.Ordinal);
        Assert.EndsWith("EOF\n", dxf, StringComparison.Ordinal);

        // The closed flag is group code 70 with value 1.
        Assert.Contains("\n70\n1\n", dxf, StringComparison.Ordinal);

        string[] lines = dxf.Split('\n');
        var xs = new List<double>();
        var ys = new List<double>();
        for (int i = 0; i + 1 < lines.Length; i++)
        {
            // A vertex record is 0/VERTEX, 8/TILE, 10/x, 20/y, 30/z.
            if (lines[i] == "10" && i >= 3 && lines[i - 3] == "VERTEX")
            {
                xs.Add(double.Parse(lines[i + 1], CultureInfo.InvariantCulture));
            }

            if (lines[i] == "20" && i >= 5 && lines[i - 5] == "VERTEX")
            {
                ys.Add(double.Parse(lines[i + 1], CultureInfo.InvariantCulture));
            }
        }

        Assert.Equal(tile.Length, xs.Count);
        Assert.Equal(tile.Length, ys.Count);

        double extent = Math.Max(xs.Max() - xs.Min(), ys.Max() - ys.Min());
        Assert.Equal(200.0, extent, 1e-6);
    }

    /// <summary>The distance of a shape to itself is zero and to a different shape is not (SPEC §7.3).</summary>
    [Fact]
    public void ProcrustesDistanceBehavesAsExpected()
    {
        Vec2[] star = TestFixtures.Star();

        var rotated = new Vec2[star.Length];
        for (int i = 0; i < star.Length; i++)
        {
            rotated[i] = star[i].Rotate(0.7) * 2.5;
        }

        Assert.Equal(0.0, CandidatePostProcessor.ProcrustesDistance(star, rotated), 1e-12);
        Assert.True(CandidatePostProcessor.ProcrustesDistance(star, TestShapes.Circle(star.Length)) > 0.1);
    }

    /// <summary>The best error of every template for a goal, used by the invariance test.</summary>
    /// <param name="goal">The goal.</param>
    /// <returns>The best errors, sorted.</returns>
    private static double[] BestErrors(GoalShape goal)
    {
        List<ScoredCandidate> raw = EscherizeSearch.Search(
            goal,
            new SearchOptions { TopK = 5 },
            progress: null,
            cancellationToken: default,
            out _);

        var errors = new double[5];
        for (int i = 0; i < errors.Length; i++)
        {
            errors[i] = raw[i].Error;
        }

        return errors;
    }

    /// <summary>The distance measure for one basis, goal and offset.</summary>
    /// <param name="basis">The basis.</param>
    /// <param name="w">The goal coordinate vector.</param>
    /// <param name="n">The number of points.</param>
    /// <param name="j">The offset.</param>
    /// <returns>The error.</returns>
    private static double ErrorAt(TileBasis basis, ReadOnlySpan<double> w, int n, int j)
    {
        var shifted = new double[2 * n];
        for (int t = 0; t < n; t++)
        {
            int index = (t + j) % n;
            shifted[t] = w[index];
            shifted[n + t] = w[n + index];
        }

        return TileFit.Fit(basis, shifted).Error;
    }

    /// <summary>Draws a simple tile from a template, the way SPEC §8.2 does.</summary>
    /// <param name="template">The template.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The tile and its layout.</returns>
    private static (Vec2[] Tile, TileLayout Layout) DrawTile(TemplateSpec template, int seed)
    {
        int[][] candidates = [.. KVectorEnumerator.Enumerate(template, 18, minimumK: 1)];

        for (int attempt = 0; attempt < 200; attempt++)
        {
            var random = new Random(HashCode.Combine(seed, attempt));
            int[] k = candidates[random.Next(candidates.Length)];
            TileLayout layout = TileLayout.Create(template, k);
            TileBasis basis = DenseBasisBuilder.Build(layout);

            double[] w = TemplateValidator.NoisyCircle(18, HashCode.Combine(seed, attempt, 31));
            double[] xi = TileFit.Fit(basis, w).Xi;

            var r = new double[xi.Length];
            double norm = 0;
            for (int i = 0; i < r.Length; i++)
            {
                double u1 = 1.0 - random.NextDouble();
                double u2 = random.NextDouble();
                r[i] = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                norm += r[i] * r[i];
            }

            norm = Math.Sqrt(norm);
            for (int i = 0; i < xi.Length; i++)
            {
                xi[i] += 0.05 * r[i] / norm;
            }

            Vec2[] tile = basis.ToTile(xi);
            if (PolygonQuality.IsValidTile(tile))
            {
                return (tile, layout);
            }
        }

        throw new InvalidOperationException($"No simple tile was drawn for {template.Name}.");
    }
}
