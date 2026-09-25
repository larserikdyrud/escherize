using System.Globalization;
using System.Text.Json;
using Escherize.Geometry;
using Escherize.Imaging;
using Escherize.Io;
using Escherize.Preprocessing;
using Escherize.Rendering;
using Escherize.Search;
using Escherize.Templates;

namespace Escherize.Cli;

/// <summary>
/// The <c>run</c> command: preprocess the input, search, and write the outputs of
/// SPEC §9.1.
/// </summary>
internal static class RunCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="output">Where progress is reported.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(CommandLine command, TextWriter output)
    {
        JobConfig job = JobConfig.Load(command);

        var imageOptions = new ImageContourOptions
        {
            Threshold = job.Threshold,
            Invert = job.Invert,
        };

        LoadedInput loaded = InputLoader.Load(job.Input, imageOptions, job.FeatureName, job.RingIndex);

        var preprocessOptions = new PreprocessOptions
        {
            PointCount = job.PointCount,
            SmoothHarmonics = job.Smooth,
            DouglasPeuckerTolerance = job.DouglasPeucker,
        };

        GoalShape goal = Preprocessor.Build(loaded.Contour, preprocessOptions);

        var searchOptions = new SearchOptions
        {
            Types = job.Types,
            TopK = job.Top,
            MinimumK = job.MinimumK,
            Diversity = job.Diversity,
            MinimumNeckWidth = job.MinimumNeck,
            Threads = job.Threads,
        };

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""
            input      {job.Input}
            kind       {loaded.Kind} ({loaded.Details})
            goal       n = {goal.PointCount}, smoothing {(job.Smooth > 0 ? $"H = {job.Smooth}" : "none")}
            search     {string.Join(", ", searchOptions.ResolveTemplates().ConvertAll(t => t.Template.Name))}
            threads    {searchOptions.Threads}
            """));

        long combinations = 0;
        foreach ((int _, TemplateSpec template) in searchOptions.ResolveTemplates())
        {
            long count = KVectorEnumerator.Count(template, goal.PointCount, searchOptions.MinimumK);
            combinations += count;
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"             {template.Name,-5} {count,12} k vectors"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"             total {combinations,12} k vectors, both orientations"));

        SearchResult result = EscherizeSearch.Run(goal, searchOptions);

        Directory.CreateDirectory(job.OutputDirectory);
        WriteOutputs(job, goal, searchOptions, result, output);

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""
            evaluations {result.Evaluations}
            elapsed     {result.Elapsed.TotalSeconds:0.00} s
            candidates  {result.Candidates.Count}
            wrote       {job.OutputDirectory}
            """));

        if (result.Candidates.Count > 0)
        {
            output.WriteLine("rank  type   rms %   neck   k");
            for (int i = 0; i < result.Candidates.Count; i++)
            {
                Candidate candidate = result.Candidates[i];
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{i + 1,4}  {candidate.Template.Name,-5} {candidate.RootErrorPercent,6:0.00}  " +
                    $"{candidate.RelativeNeckWidth,5:0.000}  [{string.Join(",", candidate.Key.K)}]"));
            }
        }

        return 0;
    }

    /// <summary>Writes every output file of SPEC §9.1.</summary>
    /// <param name="job">The job configuration.</param>
    /// <param name="goal">The normalised goal.</param>
    /// <param name="options">The search options.</param>
    /// <param name="result">The search result.</param>
    /// <param name="output">Where progress is reported.</param>
    private static void WriteOutputs(
        JobConfig job,
        GoalShape goal,
        SearchOptions options,
        SearchResult result,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        SvgWriter.WriteGoal(Path.Combine(job.OutputDirectory, "goal.svg"), goal);
        SummaryWriter.Write(
            Path.Combine(job.OutputDirectory, "summary.json"),
            new SummaryInput(job.Input, goal.PointCount, job.Smooth),
            goal,
            options,
            result);

        if (result.Candidates.Count > 0)
        {
            CandidateSvgWriter.WriteContactSheet(
                Path.Combine(job.OutputDirectory, "contact_sheet.svg"),
                result.Candidates);
        }

        int renderCount = Math.Min(job.RenderTop, result.Candidates.Count);
        for (int i = 0; i < renderCount; i++)
        {
            Candidate candidate = result.Candidates[i];
            string stem = string.Create(
                CultureInfo.InvariantCulture,
                $"rank{i + 1:00}_{candidate.Template.Name}");

            CandidateSvgWriter.WriteTile(
                Path.Combine(job.OutputDirectory, $"{stem}_tile.svg"), candidate, goal);
            CandidateSvgWriter.WriteTiling(
                Path.Combine(job.OutputDirectory, $"{stem}_tiling.svg"), candidate, job.Tiles);
            DxfWriter.Write(
                Path.Combine(job.OutputDirectory, $"{stem}_tile.dxf"), candidate.Tile, job.TileSizeMillimetres);
        }
    }
}

/// <summary>
/// The settings of a run, taken from <c>job.json</c> and then from the command line, which
/// overrides the file (SPEC §11).
/// </summary>
internal sealed record JobConfig
{
    /// <summary>The input file.</summary>
    public required string Input { get; init; }

    /// <summary>The output directory.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>The number of goal points.</summary>
    public int PointCount { get; init; } = 64;

    /// <summary>The number of retained harmonics.</summary>
    public int Smooth { get; init; } = 24;

    /// <summary>The Douglas-Peucker tolerance, when it replaces Fourier smoothing.</summary>
    public double? DouglasPeucker { get; init; }

    /// <summary>The templates to search.</summary>
    public IReadOnlyList<string> Types { get; init; } = TemplateLibrary.Names;

    /// <summary>How many candidates to report.</summary>
    public int Top { get; init; } = 20;

    /// <summary>How many candidates get their own drawings.</summary>
    public int RenderTop { get; init; } = 5;

    /// <summary>The lower bound on every k.</summary>
    public int MinimumK { get; init; }

    /// <summary>The diversity threshold.</summary>
    public double Diversity { get; init; } = 0.02;

    /// <summary>The smallest acceptable relative neck width.</summary>
    public double MinimumNeck { get; init; }

    /// <summary>The degree of parallelism.</summary>
    public int Threads { get; init; } = Environment.ProcessorCount;

    /// <summary>The longest side of the exported outline, in millimetres.</summary>
    public double TileSizeMillimetres { get; init; } = 200;

    /// <summary>How many tiles a tiling drawing holds.</summary>
    public int Tiles { get; init; } = 60;

    /// <summary>Whether the image foreground is light rather than dark.</summary>
    public bool Invert { get; init; }

    /// <summary>The explicit luminance threshold, when given.</summary>
    public int? Threshold { get; init; }

    /// <summary>The GeoJSON feature to select.</summary>
    public string? FeatureName { get; init; }

    /// <summary>The GeoJSON ring to select.</summary>
    public int? RingIndex { get; init; }

    /// <summary>Reads the configuration from the command line and an optional job file.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="CommandLineException">The configuration is incomplete or invalid.</exception>
    public static JobConfig Load(CommandLine command)
    {
        ArgumentNullException.ThrowIfNull(command);

        JsonElement? file = null;
        string? configPath = command.GetString("config");
        if (configPath is not null)
        {
            if (!File.Exists(configPath))
            {
                throw new CommandLineException($"The config file '{configPath}' does not exist.");
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            file = document.RootElement.Clone();
        }

        string? input = command.GetString("input") ?? Text(file, "input");
        if (input is null)
        {
            throw new CommandLineException("Either --input or a config file with an 'input' field is required.");
        }

        var config = new JobConfig
        {
            Input = input,
            OutputDirectory = command.GetString("out") ?? Text(file, "out") ?? "out",
            PointCount = command.GetInt32("n", Integer(file, "n") ?? 64),
            Smooth = command.GetInt32("smooth", Integer(file, "smooth") ?? 24),
            DouglasPeucker = command.GetDouble("dp") ?? Number(file, "dp"),
            Types = ParseTypes(command.GetString("types") ?? Text(file, "types")),
            Top = command.GetInt32("top", Integer(file, "top") ?? 20),
            RenderTop = command.GetInt32("render-top", Integer(file, "renderTop") ?? 5),
            MinimumK = command.GetInt32("min-k", Integer(file, "minK") ?? 0),
            Diversity = command.GetDouble("diversity") ?? Number(file, "diversity") ?? 0.02,
            MinimumNeck = command.GetDouble("min-neck") ?? Number(file, "minNeck") ?? 0.0,
            Threads = command.GetInt32("threads", Integer(file, "threads") ?? Environment.ProcessorCount),
            TileSizeMillimetres = command.GetDouble("tile-size-mm") ?? Number(file, "tileSizeMm") ?? 200,
            Tiles = command.GetInt32("tiles", Integer(file, "tiles") ?? 60),
            Invert = command.GetSwitch("invert"),
            Threshold = command.GetString("threshold") is { } t
                ? int.Parse(t, CultureInfo.InvariantCulture)
                : Integer(file, "threshold"),
            FeatureName = command.GetString("feature-name") ?? Text(file, "featureName"),
            RingIndex = command.GetString("ring-index") is { } r
                ? int.Parse(r, CultureInfo.InvariantCulture)
                : Integer(file, "ringIndex"),
        };

        command.GetString("landmarks");
        command.EnsureNoUnknownFlags();

        if (config.PointCount < 3)
        {
            throw new CommandLineException("--n must be at least 3.");
        }

        if (config.Top < 1)
        {
            throw new CommandLineException("--top must be at least 1.");
        }

        if (config.Threads < 1)
        {
            throw new CommandLineException("--threads must be at least 1.");
        }

        if (config.Threshold is < 0 or > 255)
        {
            throw new CommandLineException("--threshold must be between 0 and 255.");
        }

        return config;
    }

    /// <summary>Parses the comma separated template list, where <c>all</c> means every type.</summary>
    /// <param name="text">The list, or null.</param>
    /// <returns>The template names.</returns>
    /// <exception cref="CommandLineException">A name does not match a template.</exception>
    private static IReadOnlyList<string> ParseTypes(string? text)
    {
        if (text is null || text.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return TemplateLibrary.Names;
        }

        var names = new List<string>();
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                names.Add(TemplateLibrary.ByName(part).Name);
            }
            catch (ArgumentException)
            {
                throw new CommandLineException(
                    $"Unknown template '{part}'. Known templates: {string.Join(", ", TemplateLibrary.Names)}.");
            }
        }

        if (names.Count == 0)
        {
            throw new CommandLineException("--types listed no template.");
        }

        return names;
    }

    /// <summary>Reads a string field from the job file.</summary>
    /// <param name="file">The job file, or null.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or null.</returns>
    private static string? Text(JsonElement? file, string name) =>
        file is { } element && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Reads an integer field from the job file.</summary>
    /// <param name="file">The job file, or null.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or null.</returns>
    private static int? Integer(JsonElement? file, string name) =>
        file is { } element && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    /// <summary>Reads a floating point field from the job file.</summary>
    /// <param name="file">The job file, or null.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or null.</returns>
    private static double? Number(JsonElement? file, string name) =>
        file is { } element && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
