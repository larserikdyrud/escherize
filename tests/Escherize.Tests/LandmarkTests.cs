using System.Text.Json;
using Escherize.Geometry;
using Escherize.Io;
using Escherize.Preprocessing;
using Escherize.Search;
using Xunit;

namespace Escherize.Tests;

/// <summary>Landmark weighting and weighted reranking (SPEC §4.6, §7.4).</summary>
public sealed class LandmarkTests
{
    /// <summary>The weight is one away from every landmark and rises to W at one (SPEC §4.6).</summary>
    [Fact]
    public void WeightsPeakAtTheLandmark()
    {
        GoalShape goal = Preprocessor.Build(
            TestFixtures.Star(),
            new PreprocessOptions { PointCount = 40, SmoothHarmonics = 0 });

        // The tip of the star, in input coordinates.
        Vec2 tip = new(0, 1);
        var landmark = new Landmark("tip", tip, Weight: 5, Sigma: 0.02);
        double[] weights = Landmarks.Weights(goal, [landmark]);

        Assert.Equal(goal.PointCount, weights.Length);

        double position = Landmarks.ArcLengthOf(goal, tip);
        int nearest = (int)Math.Round(position * goal.PointCount) % goal.PointCount;

        Assert.True(weights[nearest] > 4.0, $"The weight at the landmark is {weights[nearest]}, expected above 4.");

        // Half way round the outline the landmark has no reach left.
        int opposite = (nearest + (goal.PointCount / 2)) % goal.PointCount;
        Assert.Equal(1.0, weights[opposite], 1e-6);

        foreach (double weight in weights)
        {
            Assert.True(weight >= 1.0);
        }
    }

    /// <summary>With no landmarks every point weighs the same.</summary>
    [Fact]
    public void WithoutLandmarksEveryWeightIsOne()
    {
        GoalShape goal = Preprocessor.Build(
            TestFixtures.Cat(),
            new PreprocessOptions { PointCount = 24 });

        foreach (double weight in Landmarks.Weights(goal, []))
        {
            Assert.Equal(1.0, weight);
        }
    }

    /// <summary>The arc length parameter of a point on the outline is where that point sits.</summary>
    [Fact]
    public void ArcLengthOfAGoalPointIsItsOwnPosition()
    {
        GoalShape goal = Preprocessor.Build(
            TestFixtures.Cat(),
            new PreprocessOptions { PointCount = 40, SmoothHarmonics = 0 });

        for (int t = 0; t < goal.PointCount; t += 7)
        {
            Vec2 inInputUnits = goal.Denormalize(goal.Points[t]);
            double position = Landmarks.ArcLengthOf(goal, inInputUnits);
            Assert.Equal((double)t / goal.PointCount, position, 1e-6);
        }
    }

    /// <summary>The cyclic distance wraps around the loop.</summary>
    [Fact]
    public void CyclicDistanceWraps()
    {
        Assert.Equal(0.1, Landmarks.CyclicDistance(0.05, 0.95), 1e-12);
        Assert.Equal(0.5, Landmarks.CyclicDistance(0.0, 0.5), 1e-12);
        Assert.Equal(0.0, Landmarks.CyclicDistance(0.3, 0.3), 1e-12);
    }

    /// <summary>
    /// Weighting the outline changes which candidates come out on top, which is the
    /// acceptance criterion of phase F5 (SPEC §10).
    /// </summary>
    [Fact]
    public void WeightedRankingChangesTheOrder()
    {
        GoalShape goal = Preprocessor.Build(
            TestFixtures.Cat(),
            new PreprocessOptions { PointCount = 30 });

        var options = new SearchOptions { TopK = 10 };
        List<ScoredCandidate> raw = EscherizeSearch.Search(
            goal, options, progress: null, cancellationToken: default, out _);

        List<Candidate> plain = CandidatePostProcessor.Process(goal, raw, options);

        // The two ears of the cat are what a person would look at first.
        Vec2 leftEar = goal.Denormalize(new Vec2(-0.25, 0.35));
        Vec2 rightEar = goal.Denormalize(new Vec2(0.25, 0.35));
        double[] weights = Landmarks.Weights(
            goal,
            [
                new Landmark("left ear", leftEar, Weight: 8, Sigma: 0.03),
                new Landmark("right ear", rightEar, Weight: 8, Sigma: 0.03),
            ]);

        List<Candidate> weighted = WeightedReranker.Rerank(goal, weights, raw, options);

        Assert.NotEmpty(plain);
        Assert.NotEmpty(weighted);

        // Both errors are reported, and the weighted one really was used for the ranking.
        foreach (Candidate candidate in weighted)
        {
            Assert.NotNull(candidate.WeightedError);
            Assert.True(candidate.WeightedError >= 0);
        }

        for (int i = 1; i < weighted.Count; i++)
        {
            Assert.True(
                weighted[i].WeightedError >= weighted[i - 1].WeightedError - 1e-12,
                "The weighted candidates are not ordered by the weighted error.");
        }

        string plainOrder = Describe(plain);
        string weightedOrder = Describe(weighted);
        Assert.NotEqual(plainOrder, weightedOrder);
    }

    /// <summary>A landmark file is read, with defaults filled in (SPEC §11).</summary>
    [Fact]
    public void LandmarkFileIsRead()
    {
        const string Json = """
        { "landmarks": [ { "name": "Nordkapp", "x": 25.78, "y": 71.17, "weight": 4, "sigma": 0.03 },
                         { "x": 5.0, "y": 60.0 } ] }
        """;

        using JsonDocument document = JsonDocument.Parse(Json);
        List<Landmark> landmarks = LandmarkReader.Read(document.RootElement);

        Assert.Equal(2, landmarks.Count);
        Assert.Equal("Nordkapp", landmarks[0].Name);
        Assert.Equal(25.78, landmarks[0].Position.X, 1e-12);
        Assert.Equal(4.0, landmarks[0].Weight, 1e-12);
        Assert.Equal(1.0, landmarks[1].Weight, 1e-12);
        Assert.Equal(Landmarks.DefaultSigma, landmarks[1].Sigma, 1e-12);
    }

    /// <summary>A weight below one is rejected (SPEC §4.6).</summary>
    [Fact]
    public void LandmarkWeightBelowOneIsRejected()
    {
        using JsonDocument document = JsonDocument.Parse("""{ "landmarks": [ { "x": 0, "y": 0, "weight": 0.5 } ] }""");
        Assert.Throws<InvalidDataException>(() => LandmarkReader.Read(document.RootElement));
    }

    /// <summary>A compact description of a candidate list, for comparing two orders.</summary>
    /// <param name="candidates">The candidates.</param>
    /// <returns>One line per candidate.</returns>
    private static string Describe(List<Candidate> candidates)
    {
        var text = new System.Text.StringBuilder();
        foreach (Candidate candidate in candidates)
        {
            text.Append(candidate.Template.Name)
                .Append('[')
                .Append(string.Join(",", candidate.Key.K))
                .Append("]j")
                .Append(candidate.Key.J)
                .Append(';');
        }

        return text.ToString();
    }
}
