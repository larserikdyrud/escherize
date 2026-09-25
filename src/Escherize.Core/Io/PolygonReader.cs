using System.Globalization;
using System.Text.Json;
using Escherize.Geometry;

namespace Escherize.Io;

/// <summary>Reads polygons from JSON and CSV files (SPEC §4.2).</summary>
public static class PolygonReader
{
    /// <summary>
    /// Reads a polygon from a JSON document of the form <c>{ "points": [[x,y], ...] }</c>.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The polygon points in file order.</returns>
    /// <exception cref="InvalidDataException">The document does not hold a point list.</exception>
    public static Vec2[] ReadJson(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        return ReadJson(document.RootElement);
    }

    /// <summary>Reads a polygon from a parsed JSON document.</summary>
    /// <param name="root">The root element.</param>
    /// <returns>The polygon points in file order.</returns>
    /// <exception cref="InvalidDataException">The document does not hold a point list.</exception>
    public static Vec2[] ReadJson(JsonElement root)
    {
        JsonElement array = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (!root.TryGetProperty("points", out array))
            {
                throw new InvalidDataException("The JSON document has no 'points' property.");
            }
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The 'points' property is not an array.");
        }

        var points = new List<Vec2>(array.GetArrayLength());
        foreach (JsonElement entry in array.EnumerateArray())
        {
            points.Add(entry.ValueKind == JsonValueKind.Array
                ? new Vec2(entry[0].GetDouble(), entry[1].GetDouble())
                : new Vec2(entry.GetProperty("x").GetDouble(), entry.GetProperty("y").GetDouble()));
        }

        if (points.Count < 3)
        {
            throw new InvalidDataException("A polygon needs at least three points.");
        }

        return [.. points];
    }

    /// <summary>
    /// Reads a polygon from a CSV file with one <c>x,y</c> pair per line. A header line
    /// is detected and skipped (SPEC §4.2).
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The polygon points in file order.</returns>
    /// <exception cref="InvalidDataException">A line could not be parsed.</exception>
    public static Vec2[] ReadCsv(string path) => ReadCsv(File.ReadLines(path));

    /// <summary>Reads a polygon from CSV lines.</summary>
    /// <param name="lines">The lines.</param>
    /// <returns>The polygon points in file order.</returns>
    /// <exception cref="InvalidDataException">A line could not be parsed.</exception>
    public static Vec2[] ReadCsv(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var points = new List<Vec2>();
        int lineNumber = 0;
        foreach (string rawLine in lines)
        {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] fields = line.Split([',', ';', '\t'], StringSplitOptions.TrimEntries);
            if (fields.Length < 2)
            {
                throw new InvalidDataException($"Line {lineNumber} does not hold two fields.");
            }

            bool parsedX = double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
            bool parsedY = double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y);

            if (!parsedX || !parsedY)
            {
                // A single unparsable first line is the optional header.
                if (points.Count == 0)
                {
                    continue;
                }

                throw new InvalidDataException($"Line {lineNumber} does not hold two numbers.");
            }

            points.Add(new Vec2(x, y));
        }

        if (points.Count < 3)
        {
            throw new InvalidDataException("A polygon needs at least three points.");
        }

        return [.. points];
    }

    /// <summary>Writes a polygon as <c>{ "points": [[x,y], ...] }</c>.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="polygon">The polygon.</param>
    public static void WriteJson(string path, ReadOnlySpan<Vec2> polygon)
    {
        using FileStream stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteStartArray("points");
        for (int i = 0; i < polygon.Length; i++)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(polygon[i].X);
            writer.WriteNumberValue(polygon[i].Y);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
