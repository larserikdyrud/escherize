using Escherize.Cli;
using Escherize.Io;
using Xunit;

namespace Escherize.Tests;

/// <summary>
/// End to end tests for the <c>preprocess</c> command: it must write goal.svg for a PNG,
/// a polygon JSON and a GeoJSON input (SPEC §10, phase F1).
/// </summary>
public sealed class PreprocessCommandTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "escherize-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temporary working directory.</summary>
    public PreprocessCommandTests() => Directory.CreateDirectory(_directory);

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

    /// <summary>A PNG silhouette is preprocessed into goal.svg.</summary>
    [Fact]
    public void PreprocessWritesGoalSvgForPng()
    {
        string input = Path.Combine(_directory, "circle.png");
        File.WriteAllBytes(input, TestShapes.CirclePng(40));

        AssertGoalSvgWritten(input, "png-out");
    }

    /// <summary>A polygon JSON file is preprocessed into goal.svg.</summary>
    [Fact]
    public void PreprocessWritesGoalSvgForPolygonJson()
    {
        string input = Path.Combine(_directory, "shape.json");
        PolygonReader.WriteJson(input, TestShapes.LShape());

        AssertGoalSvgWritten(input, "json-out");
    }

    /// <summary>A GeoJSON outline is preprocessed into goal.svg.</summary>
    [Fact]
    public void PreprocessWritesGoalSvgForGeoJson()
    {
        string input = Path.Combine(_directory, "land.geojson");
        File.WriteAllText(input, """
        {
          "type": "Feature",
          "properties": { "name": "Testland" },
          "geometry": {
            "type": "Polygon",
            "coordinates": [[[5.0,58.0],[11.0,58.0],[12.0,62.0],[9.0,64.0],[5.0,61.0],[5.0,58.0]]]
          }
        }
        """);

        AssertGoalSvgWritten(input, "geojson-out");
    }

    /// <summary>A CSV polygon is preprocessed into goal.svg.</summary>
    [Fact]
    public void PreprocessWritesGoalSvgForCsv()
    {
        string input = Path.Combine(_directory, "shape.csv");
        File.WriteAllLines(input, ["x,y", "0,0", "4,0", "4,1", "1,1", "1,4", "0,4"]);

        AssertGoalSvgWritten(input, "csv-out");
    }

    /// <summary>A missing input file is reported as invalid input, exit code 1 (SPEC §11).</summary>
    [Fact]
    public void MissingInputReturnsExitCodeOne()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = Program.Run(
            ["preprocess", "--input", Path.Combine(_directory, "nope.png")],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not exist", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>An unknown flag is rejected rather than silently ignored (SPEC §11).</summary>
    [Fact]
    public void UnknownFlagReturnsExitCodeOne()
    {
        string input = Path.Combine(_directory, "shape.json");
        PolygonReader.WriteJson(input, TestShapes.LShape());

        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = Program.Run(["preprocess", "--input", input, "--nonsense", "1"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown flag", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>An unknown command is rejected (SPEC §11).</summary>
    [Fact]
    public void UnknownCommandReturnsExitCodeOne()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(1, Program.Run(["frobnicate"], output, error));
        Assert.Contains("unknown command", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The help text is written to standard output with exit code 0 (SPEC §11).</summary>
    [Fact]
    public void HelpReturnsExitCodeZero()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, Program.Run(["--help"], output, error));
        Assert.Contains("preprocess", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary>Runs the command and asserts that a non trivial goal.svg was produced.</summary>
    /// <param name="input">The input file.</param>
    /// <param name="outputName">The name of the output directory.</param>
    private void AssertGoalSvgWritten(string input, string outputName)
    {
        string outputDirectory = Path.Combine(_directory, outputName);
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = Program.Run(
            ["preprocess", "--input", input, "--n", "48", "--out", outputDirectory],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());

        string goalPath = Path.Combine(outputDirectory, "goal.svg");
        Assert.True(File.Exists(goalPath), $"{goalPath} was not written.");

        string svg = File.ReadAllText(goalPath);
        Assert.StartsWith("<?xml", svg, StringComparison.Ordinal);
        Assert.Contains("<path", svg, StringComparison.Ordinal);
        Assert.Contains("n = 48", svg, StringComparison.Ordinal);
        Assert.EndsWith("</svg>\n", svg, StringComparison.Ordinal);
    }
}
