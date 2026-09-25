using System.Globalization;
using System.Text.Json;
using Escherize.Geometry;
using Escherize.Imaging;
using Escherize.Preprocessing;
using Escherize.Search;
using Escherize.Templates;

namespace Escherize.Cli;

/// <summary>
/// The <c>verify</c> command: prove that a candidate of an earlier run really tiles the
/// plane, and report the measures that decide whether it can be made.
/// </summary>
/// <remarks>
/// Every tile the search returns tiles by construction, and the test suite checks that for
/// all nine templates. This command checks one concrete result instead of the general
/// claim, which is the thing worth knowing before cutting material.
/// </remarks>
internal static class VerifyCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="output">Where the report is written.</param>
    /// <returns>0 when the tile passes, 1 when it does not.</returns>
    public static int Run(CommandLine command, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);

        string resultPath = command.GetRequiredString("result");
        int rank = command.GetInt32("rank", 0);
        command.EnsureNoUnknownFlags();

        if (!File.Exists(resultPath))
        {
            throw new CommandLineException($"The result file '{resultPath}' does not exist.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(resultPath));
        JsonElement root = document.RootElement;
        JsonElement input = root.GetProperty("input");

        string inputPath = input.GetProperty("path").GetString()
            ?? throw new CommandLineException("The result file has an empty input path.");

        if (!File.Exists(inputPath))
        {
            throw new CommandLineException(
                $"The input '{inputPath}' named by the result file does not exist, so the candidates " +
                "cannot be rebuilt.");
        }

        int pointCount = input.GetProperty("n").GetInt32();
        int smooth = input.TryGetProperty("smooth", out JsonElement smoothElement) ? smoothElement.GetInt32() : 24;

        LoadedInput loaded = InputLoader.Load(inputPath, new ImageContourOptions(), null, null);
        GoalShape goal = Preprocessor.Build(
            loaded.Contour,
            new PreprocessOptions { PointCount = pointCount, SmoothHarmonics = smooth });

        JsonElement candidates = root.GetProperty("candidates");
        int total = candidates.GetArrayLength();
        if (rank > total)
        {
            throw new CommandLineException($"The result file holds {total} candidates, so rank {rank} does not exist.");
        }

        int first = rank >= 1 ? rank : 1;
        int last = rank >= 1 ? rank : total;

        output.WriteLine("rank  type   tiles  patch  edge error  neck   area      verdict");

        bool allPassed = true;
        for (int index = first; index <= last; index++)
        {
            JsonElement entry = candidates[index - 1];
            string typeName = entry.GetProperty("type").GetString()!;

            var k = new List<int>();
            foreach (JsonElement value in entry.GetProperty("k").EnumerateArray())
            {
                k.Add(value.GetInt32());
            }

            var key = new CandidateKey(
                TemplateLibrary.IndexOf(typeName),
                [.. k],
                entry.GetProperty("j").GetInt32(),
                entry.GetProperty("reversed").GetBoolean());

            Candidate? candidate = CandidatePostProcessor.Reconstruct(
                goal, new ScoredCandidate(key, entry.GetProperty("error").GetDouble()));

            if (candidate is null)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture, $"{index,4}  {typeName,-5}  could not be rebuilt"));
                allPassed = false;
                continue;
            }

            TemplateValidationResult result = TemplateValidator.ValidateTile(
                candidate.Layout, candidate.NormalizedTile, candidate.Isometries);

            double area = Math.Abs(PolygonOps.SignedArea(candidate.Tile));
            allPassed &= result.Passed;

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{index,4}  {typeName,-5} {result.TileCount,6} {TemplateValidator.PatchDepth,6} " +
                $"{result.WorstEdgeError,11:0.0e+00} {candidate.RelativeNeckWidth,6:0.000} " +
                $"{area,9:0.###} {(result.Passed ? "TILES" : "FAILS: " + result.Reason)}"));
        }

        output.WriteLine();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""
            Checked with the test of SPEC §8.2, on each candidate as it was reconstructed:
              - the neighbour isometry of every edge maps the partner edge onto it, to
                within {TemplateValidator.EdgeCoverageTolerance:0.0e+00};
              - a patch {TemplateValidator.PatchDepth} tiles deep was grown, and
                {TemplateValidator.SampleCount} sample points spread over the tile and the
                ring of neighbours around it each fell inside exactly one tile.
            """));

        if (!allPassed)
        {
            output.WriteLine();
            output.WriteLine("At least one candidate failed. That is a bug; please report it.");
        }

        return allPassed ? 0 : 1;
    }
}
