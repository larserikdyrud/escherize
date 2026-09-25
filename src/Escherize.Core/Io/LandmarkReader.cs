using System.Text.Json;
using Escherize.Geometry;
using Escherize.Preprocessing;

namespace Escherize.Io;

/// <summary>Reads the landmark file of SPEC §11.</summary>
public static class LandmarkReader
{
    /// <summary>Reads landmarks from a JSON file.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The landmarks, in the coordinates of the input file.</returns>
    /// <exception cref="InvalidDataException">The document is malformed.</exception>
    public static List<Landmark> Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        return Read(document.RootElement);
    }

    /// <summary>Reads landmarks from a parsed JSON document.</summary>
    /// <param name="root">The root element.</param>
    /// <returns>The landmarks.</returns>
    /// <exception cref="InvalidDataException">The document is malformed.</exception>
    public static List<Landmark> Read(JsonElement root)
    {
        JsonElement array = root;
        if (root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("landmarks", out array))
        {
            throw new InvalidDataException("The landmark file has no 'landmarks' property.");
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The 'landmarks' property is not an array.");
        }

        var landmarks = new List<Landmark>(array.GetArrayLength());
        foreach (JsonElement entry in array.EnumerateArray())
        {
            if (!entry.TryGetProperty("x", out JsonElement x) || !entry.TryGetProperty("y", out JsonElement y))
            {
                throw new InvalidDataException("A landmark needs an 'x' and a 'y'.");
            }

            string name = entry.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString() ?? "landmark"
                : "landmark";

            double weight = entry.TryGetProperty("weight", out JsonElement weightElement)
                ? weightElement.GetDouble()
                : 1.0;

            double sigma = entry.TryGetProperty("sigma", out JsonElement sigmaElement)
                ? sigmaElement.GetDouble()
                : Landmarks.DefaultSigma;

            if (weight < 1)
            {
                throw new InvalidDataException($"The landmark '{name}' has weight {weight}; it must be at least 1.");
            }

            if (sigma <= 0)
            {
                throw new InvalidDataException($"The landmark '{name}' has sigma {sigma}; it must be positive.");
            }

            landmarks.Add(new Landmark(name, new Vec2(x.GetDouble(), y.GetDouble()), weight, sigma));
        }

        if (landmarks.Count == 0)
        {
            throw new InvalidDataException("The landmark file lists no landmark.");
        }

        return landmarks;
    }
}
