using System.Globalization;
using Escherize.Geometry;
using Escherize.Imaging;
using Escherize.Preprocessing;
using Escherize.Rendering;

namespace Escherize.Cli;

/// <summary>
/// The <c>preprocess</c> command: read an input shape, smooth, resample and normalise it,
/// and write <c>goal.svg</c> (SPEC §4, §9.1, §11).
/// </summary>
internal static class PreprocessCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="output">Where progress is reported.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(CommandLine command, TextWriter output)
    {
        string input = command.GetRequiredString("input");
        int pointCount = command.GetInt32("n", 64);
        int harmonics = command.GetInt32("smooth", ContourSmoothing.DefaultHarmonics);
        double? douglasPeucker = command.GetDouble("dp");
        bool invert = command.GetSwitch("invert");
        double? threshold = command.GetDouble("threshold");
        int? ringIndex = command.GetString("ring-index") is { } ring
            ? int.Parse(ring, CultureInfo.InvariantCulture)
            : null;
        string? featureName = command.GetString("feature-name");
        string outputDirectory = command.GetString("out") ?? "out";
        command.EnsureNoUnknownFlags();

        if (pointCount < 3)
        {
            throw new CommandLineException("--n must be at least 3.");
        }

        if (threshold is < 0 or > 255)
        {
            throw new CommandLineException("--threshold must be between 0 and 255.");
        }

        var imageOptions = new ImageContourOptions
        {
            Threshold = threshold is { } t ? (int)t : null,
            Invert = invert,
        };

        LoadedInput loaded = InputLoader.Load(input, imageOptions, featureName, ringIndex);

        var preprocessOptions = new PreprocessOptions
        {
            PointCount = pointCount,
            SmoothHarmonics = harmonics,
            DouglasPeuckerTolerance = douglasPeucker,
        };

        GoalShape goal = Preprocessor.Build(loaded.Contour, preprocessOptions);

        Directory.CreateDirectory(outputDirectory);
        string goalPath = Path.Combine(outputDirectory, "goal.svg");
        SvgWriter.WriteGoal(goalPath, goal);

        double area = PolygonOps.SignedArea(goal.Points);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""
            input      {input}
            kind       {loaded.Kind} ({loaded.Details})
            smoothing  {(douglasPeucker is { } dp ? $"Douglas-Peucker {dp}" : harmonics > 0 ? $"Fourier H = {harmonics}" : "none")}
            goal       n = {goal.PointCount}, signed area = {area:0.######}, scale = {goal.Scale:0.######}
            centroid   {goal.Centroid.X:0.######}, {goal.Centroid.Y:0.######}
            wrote      {goalPath}
            """));

        return 0;
    }
}
