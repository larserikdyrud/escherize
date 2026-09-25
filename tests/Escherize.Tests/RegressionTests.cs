using System.Globalization;
using System.Text;
using System.Text.Json;
using Escherize.Cli;
using Escherize.Geometry;
using Escherize.Preprocessing;
using Escherize.Search;
using Xunit;

namespace Escherize.Tests;

/// <summary>Determinism and regression tests (SPEC §8.7).</summary>
public sealed class RegressionTests : IDisposable
{
    /// <summary>The number of goal points the snapshots were taken at.</summary>
    private const int PointCount = 30;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "escherize-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temporary working directory.</summary>
    public RegressionTests() => Directory.CreateDirectory(_directory);

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A locked temporary file must not fail the test run.
        }
    }

    /// <summary>
    /// The same input with one thread and with eight gives the same summary.json
    /// (SPEC §8.7).
    /// </summary>
    [Fact]
    public void SummaryIsIdenticalAcrossThreadCounts()
    {
        string input = TestFixtures.EnsureWritten("lshape");

        string single = RunSearch(input, threads: 1, name: "t1");
        string many = RunSearch(input, threads: 8, name: "t8");

        // The elapsed time is the one field that cannot match: a run on one thread really
        // does take longer than a run on eight. It is excluded here and nowhere else; see
        // DECISIONS.md.
        Assert.Equal(WithoutElapsed(single), WithoutElapsed(many));
    }

    /// <summary>
    /// The top five candidates for the four fixed shapes match the recorded snapshot
    /// (SPEC §8.7).
    /// </summary>
    /// <param name="fixtureName">The fixture.</param>
    [Theory]
    [InlineData("star")]
    [InlineData("lshape")]
    [InlineData("narrowwaist")]
    [InlineData("cat")]
    public void TopCandidatesMatchTheSnapshot(string fixtureName)
    {
        string actual = Describe(fixtureName);
        string path = Path.Combine(TestFixtures.Directory(), "snapshots", $"{fixtureName}.txt");

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            Assert.Fail(
                $"The snapshot for '{fixtureName}' did not exist and has been written to {path}. " +
                "Inspect it, commit it, and run the test again.");
        }

        string expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.Equal(expected, actual.ReplaceLineEndings("\n"));
    }

    /// <summary>The fixtures are stable: regenerating them gives the same points.</summary>
    [Fact]
    public void FixturesAreDeterministic()
    {
        foreach (string name in TestFixtures.Names)
        {
            Vec2[] first = TestFixtures.Build(name);
            Vec2[] second = TestFixtures.Build(name);
            Assert.Equal(first, second);
            Assert.True(PolygonOps.SignedArea(first) > 0, $"Fixture '{name}' is not positively oriented.");

            // The shapes are also kept as JSON under tests/fixtures (SPEC §2).
            Assert.True(File.Exists(TestFixtures.EnsureWritten(name)));
        }
    }

    /// <summary>Runs the top five of a fixture and renders it as stable text (SPEC §8.7).</summary>
    /// <param name="fixtureName">The fixture.</param>
    /// <returns>One line per candidate.</returns>
    private static string Describe(string fixtureName)
    {
        GoalShape goal = Preprocessor.Build(
            TestFixtures.Build(fixtureName),
            new PreprocessOptions { PointCount = PointCount });

        SearchResult result = EscherizeSearch.Run(goal, new SearchOptions { TopK = 5 });

        var text = new StringBuilder();
        foreach (Candidate candidate in result.Candidates)
        {
            text.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{candidate.Template.Name} k=[{string.Join(",", candidate.Key.K)}] " +
                $"j={candidate.Key.J} rev={(candidate.Key.Reversed ? 1 : 0)} " +
                $"e={Math.Round(candidate.Error, 9).ToString("0.000000000", CultureInfo.InvariantCulture)}"));
        }

        return text.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>Runs the CLI and returns the summary it wrote.</summary>
    /// <param name="input">The input file.</param>
    /// <param name="threads">The thread count.</param>
    /// <param name="name">The name of the output directory.</param>
    /// <returns>The contents of summary.json.</returns>
    private string RunSearch(string input, int threads, string name)
    {
        string outputDirectory = Path.Combine(_directory, name);
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = Program.Run(
            [
                "run",
                "--input", input,
                "--n", PointCount.ToString(CultureInfo.InvariantCulture),
                "--top", "10",
                "--render-top", "0",
                "--threads", threads.ToString(CultureInfo.InvariantCulture),
                "--out", outputDirectory,
            ],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        return File.ReadAllText(Path.Combine(outputDirectory, "summary.json"));
    }

    /// <summary>Removes the elapsed time line from a summary.</summary>
    /// <param name="summary">The summary text.</param>
    /// <returns>The summary without its timing.</returns>
    private static string WithoutElapsed(string summary)
    {
        var kept = new StringBuilder();
        foreach (string line in summary.ReplaceLineEndings("\n").Split('\n'))
        {
            if (!line.TrimStart().StartsWith("\"seconds\"", StringComparison.Ordinal))
            {
                kept.AppendLine(line);
            }
        }

        return kept.ToString();
    }
}
