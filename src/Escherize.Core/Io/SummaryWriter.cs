using System.Text.Json;
using Escherize.Geometry;
using Escherize.Preprocessing;
using Escherize.Search;
using Escherize.Templates;

namespace Escherize.Io;

/// <summary>Describes the input a run was given, for the summary (SPEC §9.2).</summary>
/// <param name="Path">The input file.</param>
/// <param name="PointCount">The number of goal points, n.</param>
/// <param name="Smooth">The number of retained harmonics.</param>
public sealed record SummaryInput(string Path, int PointCount, int Smooth);

/// <summary>Writes <c>summary.json</c> (SPEC §9.2).</summary>
public static class SummaryWriter
{
    /// <summary>The number of decimals coordinates are written with.</summary>
    private const int CoordinateDigits = 9;

    /// <summary>Writes the summary.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="input">The input description.</param>
    /// <param name="goal">The normalised goal.</param>
    /// <param name="options">The search options.</param>
    /// <param name="result">The search result.</param>
    public static void Write(
        string path,
        SummaryInput input,
        GoalShape goal,
        SearchOptions options,
        SearchResult result)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(result);

        using FileStream stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        writer.WriteStartObject("input");
        writer.WriteString("path", input.Path);
        writer.WriteNumber("n", input.PointCount);
        writer.WriteNumber("smooth", input.Smooth);
        writer.WriteStartArray("centroid");
        writer.WriteNumberValue(Round(goal.Centroid.X));
        writer.WriteNumberValue(Round(goal.Centroid.Y));
        writer.WriteEndArray();
        writer.WriteNumber("scale", Round(goal.Scale));
        writer.WriteEndObject();

        writer.WriteStartObject("search");
        writer.WriteStartArray("types");
        foreach ((int _, TemplateSpec template) in options.ResolveTemplates())
        {
            writer.WriteStringValue(template.Name);
        }

        writer.WriteEndArray();
        writer.WriteNumber("evaluations", result.Evaluations);

        // The elapsed time is deliberately left out of the comparison that SPEC §8.7
        // makes, so it is rounded hard enough to stay stable between runs.
        writer.WriteNumber("seconds", Math.Round(result.Elapsed.TotalSeconds, 1));
        writer.WriteEndObject();

        writer.WriteStartArray("candidates");
        for (int i = 0; i < result.Candidates.Count; i++)
        {
            WriteCandidate(writer, result.Candidates[i], i + 1);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Writes one candidate object (SPEC §9.2).</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="candidate">The candidate.</param>
    /// <param name="rank">The one based rank.</param>
    private static void WriteCandidate(Utf8JsonWriter writer, Candidate candidate, int rank)
    {
        writer.WriteStartObject();
        writer.WriteNumber("rank", rank);
        writer.WriteString("type", candidate.Template.Name);
        writer.WriteString("heesch", candidate.Template.Heesch);

        writer.WriteStartArray("k");
        foreach (int value in candidate.Key.K)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();

        writer.WriteNumber("j", candidate.Key.J);
        writer.WriteBoolean("reversed", candidate.Key.Reversed);
        writer.WriteNumber("error", Round(candidate.Error));
        writer.WriteNumber("rmsPercent", Round(candidate.RootErrorPercent));

        if (candidate.WeightedError is { } weighted)
        {
            writer.WriteNumber("weightedError", Round(weighted));
        }
        else
        {
            writer.WriteNull("weightedError");
        }

        writer.WriteNumber("neckWidthRel", Round(candidate.RelativeNeckWidth));

        writer.WriteStartArray("vertices");
        foreach (int index in candidate.VertexIndices)
        {
            writer.WriteNumberValue(index);
        }

        writer.WriteEndArray();

        writer.WriteStartArray("tile");
        foreach (Vec2 point in candidate.Tile)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(Round(point.X));
            writer.WriteNumberValue(Round(point.Y));
            writer.WriteEndArray();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("edges");
        TileLayout layout = candidate.Layout;
        for (int s = 0; s < layout.VertexCount; s++)
        {
            EdgeSpec edge = candidate.Template.Edges[s];
            writer.WriteStartObject();
            writer.WriteString("kind", edge.Kind.ToString());
            writer.WriteString("pair", edge.PairId < 0 ? null : ((char)('A' + edge.PairId)).ToString());
            writer.WriteNumber("from", layout.EdgePointIndex(s, 0));
            writer.WriteNumber("to", layout.EdgePointIndex(s, layout.EdgeK(s) + 1));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("isometries");
        for (int s = 0; s < candidate.Isometries.Length; s++)
        {
            Isometry m = candidate.Isometries[s];
            writer.WriteStartObject();
            writer.WriteNumber("edge", s);
            writer.WriteStartArray("m");
            foreach (double value in (double[])[m.A, m.B, m.C, m.D, m.Tx, m.Ty])
            {
                writer.WriteNumberValue(Round(value));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Rounds a value so that the file is stable between runs and between thread counts
    /// (SPEC §8.7).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The rounded value.</returns>
    private static double Round(double value)
    {
        double rounded = Math.Round(value, CoordinateDigits, MidpointRounding.AwayFromZero);

        // Negative zero would print differently from zero.
        return rounded == 0 ? 0 : rounded;
    }
}
