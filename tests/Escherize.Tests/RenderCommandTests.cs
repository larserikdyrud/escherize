using System.Globalization;
using System.Text.Json;
using Escherize.Cli;
using Escherize.Io;
using Xunit;

namespace Escherize.Tests;

/// <summary>End to end tests for the run and render commands of phase F5 (SPEC §10, §11).</summary>
public sealed class RenderCommandTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "escherize-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temporary working directory.</summary>
    public RenderCommandTests() => Directory.CreateDirectory(_directory);

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

    /// <summary>The render command draws a candidate of an earlier run again (SPEC §11).</summary>
    [Fact]
    public void RenderRedrawsACandidate()
    {
        string summary = RunSearch("base", extra: []);

        string outputDirectory = Path.Combine(_directory, "redrawn");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = Program.Run(
            ["render", "--result", summary, "--rank", "2", "--tiles", "40", "--out", outputDirectory],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());

        string[] written = Directory.GetFiles(outputDirectory);
        Assert.Contains(written, f => f.EndsWith("_tile.svg", StringComparison.Ordinal));
        Assert.Contains(written, f => f.EndsWith("_tiling.svg", StringComparison.Ordinal));
        Assert.Contains(written, f => f.EndsWith("_tile.dxf", StringComparison.Ordinal));

        // The rebuilt candidate must be the one the summary named.
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(summary));
        string type = document.RootElement.GetProperty("candidates")[1].GetProperty("type").GetString()!;
        Assert.Contains(written, f => f.Contains(type, StringComparison.Ordinal));
    }

    /// <summary>A rank beyond the list is reported as invalid input (SPEC §11).</summary>
    [Fact]
    public void RenderRejectsAnOutOfRangeRank()
    {
        string summary = RunSearch("range", extra: []);

        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = Program.Run(
            ["render", "--result", summary, "--rank", "999", "--out", Path.Combine(_directory, "none")],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not exist", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A missing result file is reported as invalid input.</summary>
    [Fact]
    public void RenderRejectsAMissingResult()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = Program.Run(
            ["render", "--result", Path.Combine(_directory, "nope.json")],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not exist", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The neck width filter drops tiles that are too thin (SPEC §7.3).</summary>
    [Fact]
    public void MinimumNeckWidthFiltersCandidates()
    {
        string relaxed = RunSearch("relaxed", extra: ["--min-neck", "0.0"]);
        double[] necks = NeckWidths(relaxed);
        Assert.NotEmpty(necks);

        // Pick a bound that the best candidate fails, so the filter has to bite.
        double bound = necks.Max() - 1e-9;
        if (necks[0] >= bound)
        {
            return;
        }

        string strict = RunSearch(
            "strict",
            extra: ["--min-neck", bound.ToString("0.#####", CultureInfo.InvariantCulture)]);

        foreach (double neck in NeckWidths(strict))
        {
            Assert.True(neck >= bound - 1e-9, $"A candidate with neck {neck} survived a bound of {bound}.");
        }
    }

    /// <summary>A run with landmarks reports both errors and ranks by the weighted one (SPEC §7.4).</summary>
    [Fact]
    public void LandmarkRunReportsBothErrors()
    {
        string landmarkPath = Path.Combine(_directory, "lm.json");
        File.WriteAllText(landmarkPath, """
        { "landmarks": [ { "name": "tip", "x": 0.0, "y": 1.0, "weight": 8, "sigma": 0.03 } ] }
        """);

        string summary = RunSearch("weighted", extra: ["--landmarks", landmarkPath]);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(summary));
        JsonElement candidates = document.RootElement.GetProperty("candidates");
        Assert.True(candidates.GetArrayLength() > 0);

        double previous = -1;
        foreach (JsonElement candidate in candidates.EnumerateArray())
        {
            JsonElement weighted = candidate.GetProperty("weightedError");
            Assert.NotEqual(JsonValueKind.Null, weighted.ValueKind);

            double value = weighted.GetDouble();
            Assert.True(value >= previous - 1e-12, "The candidates are not ordered by the weighted error.");
            previous = value;

            Assert.True(candidate.GetProperty("error").GetDouble() >= 0);
        }
    }

    /// <summary>Runs a search on the star fixture and returns the path of its summary.</summary>
    /// <param name="name">The name of the output directory.</param>
    /// <param name="extra">Extra command line arguments.</param>
    /// <returns>The path of summary.json.</returns>
    private string RunSearch(string name, string[] extra)
    {
        string input = Path.Combine(_directory, "star.json");
        if (!File.Exists(input))
        {
            PolygonReader.WriteJson(input, TestFixtures.Star());
        }

        string outputDirectory = Path.Combine(_directory, name);
        var output = new StringWriter();
        var error = new StringWriter();

        string[] arguments =
        [
            "run", "--input", input, "--n", "24", "--top", "6", "--render-top", "0", "--out", outputDirectory,
            .. extra,
        ];

        Assert.Equal(0, Program.Run(arguments, output, error));
        Assert.Equal(string.Empty, error.ToString());
        return Path.Combine(outputDirectory, "summary.json");
    }

    /// <summary>The neck widths recorded in a summary, in rank order.</summary>
    /// <param name="summaryPath">The summary file.</param>
    /// <returns>The neck widths.</returns>
    private static double[] NeckWidths(string summaryPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(summaryPath));
        var values = new List<double>();
        foreach (JsonElement candidate in document.RootElement.GetProperty("candidates").EnumerateArray())
        {
            values.Add(candidate.GetProperty("neckWidthRel").GetDouble());
        }

        return [.. values];
    }
}
