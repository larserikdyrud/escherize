using System.Globalization;
using System.Text.Json;
using Escherize.Imaging;
using Escherize.Preprocessing;
using Escherize.Rendering;
using Escherize.Search;
using Escherize.Templates;

namespace Escherize.Cli;

/// <summary>
/// The <c>render</c> command: draw one candidate of an earlier run again, with different
/// settings (SPEC §11).
/// </summary>
/// <remarks>
/// The summary records which template, k vector, offset and orientation a candidate is, and
/// where its input came from. That is enough to rebuild the candidate exactly, so the
/// drawings come from the same code the run used rather than from the stored coordinates.
/// </remarks>
internal static class RenderCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="output">Where progress is reported.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(CommandLine command, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);

        string resultPath = command.GetRequiredString("result");
        int rank = command.GetInt32("rank", 1);
        int tiles = command.GetInt32("tiles", 60);
        double tileSize = command.GetDouble("tile-size-mm") ?? 200;
        string outputDirectory = command.GetString("out") ?? Path.GetDirectoryName(resultPath) ?? ".";
        command.EnsureNoUnknownFlags();

        if (!File.Exists(resultPath))
        {
            throw new CommandLineException($"The result file '{resultPath}' does not exist.");
        }

        if (rank < 1)
        {
            throw new CommandLineException("--rank must be at least 1.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(resultPath));
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("input", out JsonElement inputElement)
            || !inputElement.TryGetProperty("path", out JsonElement pathElement))
        {
            throw new CommandLineException("The result file has no input path.");
        }

        string inputPath = pathElement.GetString()
            ?? throw new CommandLineException("The result file has an empty input path.");

        if (!File.Exists(inputPath))
        {
            throw new CommandLineException(
                $"The input '{inputPath}' named by the result file does not exist, so the candidate " +
                "cannot be rebuilt.");
        }

        int pointCount = inputElement.GetProperty("n").GetInt32();
        int smooth = inputElement.TryGetProperty("smooth", out JsonElement smoothElement)
            ? smoothElement.GetInt32()
            : 24;

        if (!root.TryGetProperty("candidates", out JsonElement candidates)
            || candidates.ValueKind != JsonValueKind.Array)
        {
            throw new CommandLineException("The result file lists no candidates.");
        }

        if (rank > candidates.GetArrayLength())
        {
            throw new CommandLineException(
                $"The result file holds {candidates.GetArrayLength()} candidates, so rank {rank} does not exist.");
        }

        JsonElement chosen = candidates[rank - 1];
        string typeName = chosen.GetProperty("type").GetString()
            ?? throw new CommandLineException("The candidate has no type.");

        var k = new List<int>();
        foreach (JsonElement value in chosen.GetProperty("k").EnumerateArray())
        {
            k.Add(value.GetInt32());
        }

        int offset = chosen.GetProperty("j").GetInt32();
        bool reversed = chosen.GetProperty("reversed").GetBoolean();

        LoadedInput loaded = InputLoader.Load(inputPath, new ImageContourOptions(), null, null);
        GoalShape goal = Preprocessor.Build(
            loaded.Contour,
            new PreprocessOptions { PointCount = pointCount, SmoothHarmonics = smooth });

        int typeIndex = TemplateLibrary.IndexOf(typeName);
        double error = chosen.GetProperty("error").GetDouble();

        Candidate candidate = CandidatePostProcessor.Reconstruct(
            goal,
            new ScoredCandidate(new CandidateKey(typeIndex, [.. k], offset, reversed), error))
            ?? throw new CommandLineException($"The candidate at rank {rank} could not be rebuilt.");

        Directory.CreateDirectory(outputDirectory);
        string stem = string.Create(CultureInfo.InvariantCulture, $"rank{rank:00}_{typeName}");

        string tilePath = Path.Combine(outputDirectory, $"{stem}_tile.svg");
        string tilingPath = Path.Combine(outputDirectory, $"{stem}_tiling.svg");
        string dxfPath = Path.Combine(outputDirectory, $"{stem}_tile.dxf");

        CandidateSvgWriter.WriteTile(tilePath, candidate, goal);
        CandidateSvgWriter.WriteTiling(tilingPath, candidate, tiles);
        DxfWriter.Write(dxfPath, candidate.Tile, tileSize);

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""
            result     {resultPath}
            rank       {rank} of {candidates.GetArrayLength()}
            candidate  {typeName} k=[{string.Join(",", k)}] j={offset}{(reversed ? " reversed" : string.Empty)}
            rms        {candidate.RootErrorPercent:0.00} %, neck {candidate.RelativeNeckWidth:0.###}
            wrote      {tilePath}
                       {tilingPath}
                       {dxfPath}
            """));

        return 0;
    }
}
