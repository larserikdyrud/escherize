using System.Globalization;
using Escherize.Geometry;
using Escherize.Imaging;
using Escherize.Io;

namespace Escherize.Cli;

/// <summary>How the input contour was obtained.</summary>
internal enum InputKind
{
    /// <summary>A raster silhouette (SPEC §4.1).</summary>
    Image,

    /// <summary>A polygon in JSON or CSV (SPEC §4.2).</summary>
    Polygon,

    /// <summary>A country or region outline in GeoJSON (SPEC §4.3).</summary>
    GeoJson,
}

/// <summary>The raw contour read from an input file, before smoothing and resampling.</summary>
/// <param name="Contour">The contour, y-up.</param>
/// <param name="Kind">How it was obtained.</param>
/// <param name="Details">A short human readable note about the import.</param>
/// <param name="ToContourSpace">
/// Maps a position written in the coordinates of the input file into the frame the contour
/// lives in, which is what a landmark position has to go through (SPEC §4.6, §11). It flips
/// y for an image, projects for GeoJSON, and leaves a plain polygon alone.
/// </param>
internal sealed record LoadedInput(
    Vec2[] Contour,
    InputKind Kind,
    string Details,
    Func<Vec2, Vec2> ToContourSpace);

/// <summary>Dispatches an input file to the reader that matches it (SPEC §4.1 to §4.3).</summary>
internal static class InputLoader
{
    /// <summary>Reads the contour from a file, choosing the reader by extension and content.</summary>
    /// <param name="path">The input file.</param>
    /// <param name="options">The image threshold options.</param>
    /// <param name="featureName">The GeoJSON feature to select, or null for the first.</param>
    /// <param name="ringIndex">The GeoJSON ring to select, or null for the largest.</param>
    /// <returns>The loaded contour.</returns>
    /// <exception cref="CommandLineException">The file does not exist or has no supported extension.</exception>
    public static LoadedInput Load(string path, ImageContourOptions options, string? featureName, int? ringIndex)
    {
        if (!File.Exists(path))
        {
            throw new CommandLineException($"The input file '{path}' does not exist.");
        }

        string extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".png":
            case ".jpg":
            case ".jpeg":
            case ".bmp":
            case ".tga":
            case ".gif":
            {
                PngContourExtractor.Result result = PngContourExtractor.Read(path, options);
                string details =
                    $"{result.Mask.Width}x{result.Mask.Height} px, threshold {result.Threshold}, " +
                    $"{result.Contour.Length} contour points";

                // Image rows run downwards and the contour was mirrored on import.
                return new LoadedInput(
                    result.Contour, InputKind.Image, details, static p => new Vec2(p.X, -p.Y));
            }

            case ".csv":
            {
                Vec2[] contour = PolygonReader.ReadCsv(path);
                return new LoadedInput(contour, InputKind.Polygon, $"{contour.Length} points", Identity);
            }

            case ".geojson":
            {
                return LoadGeoJson(path, featureName, ringIndex);
            }

            case ".json":
            {
                // A .json file may hold either a plain polygon or GeoJSON.
                return LooksLikeGeoJson(path)
                    ? LoadGeoJson(path, featureName, ringIndex)
                    : LoadPolygonJson(path);
            }

            default:
                throw new CommandLineException(
                    $"Unsupported input extension '{extension}'. Use .png, .json, .csv or .geojson.");
        }
    }

    /// <summary>Reads a polygon JSON file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The loaded contour.</returns>
    private static LoadedInput LoadPolygonJson(string path)
    {
        Vec2[] contour = PolygonReader.ReadJson(path);
        return new LoadedInput(contour, InputKind.Polygon, $"{contour.Length} points", Identity);
    }

    /// <summary>Reads and projects a GeoJSON file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="featureName">The feature to select, or null for the first.</param>
    /// <param name="ringIndex">The ring to select, or null for the largest.</param>
    /// <returns>The loaded contour.</returns>
    private static LoadedInput LoadGeoJson(string path, string? featureName, int? ringIndex)
    {
        GeoJsonReader.Result result = GeoJsonReader.Read(path, featureName, ringIndex);
        string centre = string.Create(
            CultureInfo.InvariantCulture,
            $"{result.Projection.CenterLongitudeDegrees:0.###}, {result.Projection.CenterLatitudeDegrees:0.###}");
        string details =
            $"ring {result.RingIndex} of {result.RingCount}, {result.Contour.Length} points, " +
            $"Lambert azimuthal equal-area centred on {centre}";

        // A landmark is given as longitude and latitude and goes through the same
        // projection as the outline (SPEC §11).
        LambertAzimuthalEqualArea projection = result.Projection;
        return new LoadedInput(
            result.Contour, InputKind.GeoJson, details, p => projection.Project(p.X, p.Y));
    }

    /// <summary>The transform used when the input file is already in contour coordinates.</summary>
    private static Vec2 Identity(Vec2 point) => point;

    /// <summary>Peeks at a JSON file to tell GeoJSON from a plain polygon.</summary>
    /// <param name="path">The file.</param>
    /// <returns>True when the file looks like GeoJSON.</returns>
    private static bool LooksLikeGeoJson(string path)
    {
        using var reader = new StreamReader(path);
        Span<char> buffer = stackalloc char[512];
        int read = reader.ReadBlock(buffer);
        ReadOnlySpan<char> head = buffer[..read];

        return head.Contains("FeatureCollection", StringComparison.OrdinalIgnoreCase)
            || head.Contains("MultiPolygon", StringComparison.OrdinalIgnoreCase)
            || head.Contains("\"geometry\"", StringComparison.OrdinalIgnoreCase)
            || head.Contains("\"coordinates\"", StringComparison.OrdinalIgnoreCase);
    }
}
