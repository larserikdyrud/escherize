using System.Text.Json;
using Escherize.Geometry;

namespace Escherize.Io;

/// <summary>
/// Reads country and region outlines from GeoJSON and projects them (SPEC §4.3).
/// Supported geometries are Polygon and MultiPolygon, wrapped in a Feature or a
/// FeatureCollection or given on their own.
/// </summary>
public static class GeoJsonReader
{
    /// <summary>The result of reading a GeoJSON file.</summary>
    /// <param name="Contour">The chosen ring, projected to kilometres and y-up.</param>
    /// <param name="Projection">The projection that was used, centred on the bounding box centre.</param>
    /// <param name="RingIndex">The index of the chosen ring among the exterior rings.</param>
    /// <param name="RingCount">The number of exterior rings that were found.</param>
    public sealed record Result(Vec2[] Contour, LambertAzimuthalEqualArea Projection, int RingIndex, int RingCount);

    /// <summary>Reads a GeoJSON file and returns the projected outline.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="featureName">
    /// The value of a <c>name</c>-like property that selects a feature from a collection.
    /// When null the first feature is used.
    /// </param>
    /// <param name="ringIndex">
    /// The exterior ring to use. When null the ring with the largest projected area is
    /// chosen, which is the mainland for a country with islands.
    /// </param>
    /// <returns>The projected outline and the projection.</returns>
    /// <exception cref="InvalidDataException">The file holds no usable polygon.</exception>
    public static Result Read(string path, string? featureName = null, int? ringIndex = null)
    {
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        return Read(document.RootElement, featureName, ringIndex);
    }

    /// <summary>Reads a parsed GeoJSON document and returns the projected outline.</summary>
    /// <param name="root">The root element.</param>
    /// <param name="featureName">The feature to select from a collection, or null for the first.</param>
    /// <param name="ringIndex">The exterior ring to use, or null for the largest.</param>
    /// <returns>The projected outline and the projection.</returns>
    /// <exception cref="InvalidDataException">The document holds no usable polygon.</exception>
    public static Result Read(JsonElement root, string? featureName = null, int? ringIndex = null)
    {
        JsonElement geometry = SelectGeometry(root, featureName);
        List<Vec2[]> rings = ReadExteriorRings(geometry);
        if (rings.Count == 0)
        {
            throw new InvalidDataException("The GeoJSON file contains no polygon ring.");
        }

        LambertAzimuthalEqualArea projection = CreateProjection(rings);

        Vec2[][] projected = new Vec2[rings.Count][];
        for (int i = 0; i < rings.Count; i++)
        {
            projected[i] = projection.Project(rings[i]);
        }

        int chosen;
        if (ringIndex is { } requested)
        {
            if (requested < 0 || requested >= projected.Length)
            {
                throw new InvalidDataException(
                    $"Ring index {requested} is out of range; the file has {projected.Length} exterior rings.");
            }

            chosen = requested;
        }
        else
        {
            chosen = 0;
            double bestArea = -1;
            for (int i = 0; i < projected.Length; i++)
            {
                double area = Math.Abs(PolygonOps.SignedArea(projected[i]));
                if (area > bestArea)
                {
                    bestArea = area;
                    chosen = i;
                }
            }
        }

        return new Result(projected[chosen], projection, chosen, projected.Length);
    }

    /// <summary>Builds the projection centred on the bounding box centre of all rings (SPEC §4.3).</summary>
    /// <param name="rings">The unprojected rings, in longitude and latitude degrees.</param>
    /// <returns>The projection.</returns>
    private static LambertAzimuthalEqualArea CreateProjection(List<Vec2[]> rings)
    {
        double minLongitude = double.PositiveInfinity;
        double maxLongitude = double.NegativeInfinity;
        double minLatitude = double.PositiveInfinity;
        double maxLatitude = double.NegativeInfinity;

        foreach (Vec2[] ring in rings)
        {
            foreach (Vec2 point in ring)
            {
                minLongitude = Math.Min(minLongitude, point.X);
                maxLongitude = Math.Max(maxLongitude, point.X);
                minLatitude = Math.Min(minLatitude, point.Y);
                maxLatitude = Math.Max(maxLatitude, point.Y);
            }
        }

        return new LambertAzimuthalEqualArea(
            (minLongitude + maxLongitude) * 0.5,
            (minLatitude + maxLatitude) * 0.5);
    }

    /// <summary>Unwraps Feature and FeatureCollection nodes down to a geometry node.</summary>
    /// <param name="element">The element to unwrap.</param>
    /// <param name="featureName">The feature to select from a collection, or null for the first.</param>
    /// <returns>The geometry element.</returns>
    /// <exception cref="InvalidDataException">No matching geometry was found.</exception>
    private static JsonElement SelectGeometry(JsonElement element, string? featureName)
    {
        string type = element.TryGetProperty("type", out JsonElement typeElement)
            ? typeElement.GetString() ?? string.Empty
            : string.Empty;

        switch (type)
        {
            case "FeatureCollection":
            {
                if (!element.TryGetProperty("features", out JsonElement features)
                    || features.ValueKind != JsonValueKind.Array
                    || features.GetArrayLength() == 0)
                {
                    throw new InvalidDataException("The FeatureCollection has no features.");
                }

                if (featureName is null)
                {
                    return SelectGeometry(features[0], null);
                }

                foreach (JsonElement feature in features.EnumerateArray())
                {
                    if (MatchesName(feature, featureName))
                    {
                        return SelectGeometry(feature, null);
                    }
                }

                throw new InvalidDataException($"No feature named '{featureName}' was found.");
            }

            case "Feature":
            {
                if (!element.TryGetProperty("geometry", out JsonElement geometry)
                    || geometry.ValueKind == JsonValueKind.Null)
                {
                    throw new InvalidDataException("The feature has no geometry.");
                }

                return geometry;
            }

            case "Polygon":
            case "MultiPolygon":
                return element;

            default:
                throw new InvalidDataException($"Unsupported GeoJSON type '{type}'.");
        }
    }

    /// <summary>Tests whether a feature carries a name-like property with the given value.</summary>
    /// <param name="feature">The feature.</param>
    /// <param name="featureName">The value to match, case insensitively.</param>
    /// <returns>True when the feature matches.</returns>
    private static bool MatchesName(JsonElement feature, string featureName)
    {
        if (!feature.TryGetProperty("properties", out JsonElement properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty property in properties.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            bool nameLike = property.Name.Contains("name", StringComparison.OrdinalIgnoreCase)
                || property.Name.Equals("admin", StringComparison.OrdinalIgnoreCase)
                || property.Name.Equals("id", StringComparison.OrdinalIgnoreCase);

            if (nameLike && string.Equals(property.Value.GetString(), featureName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Collects the exterior ring of every polygon in the geometry.</summary>
    /// <param name="geometry">A Polygon or MultiPolygon element.</param>
    /// <returns>The exterior rings, with the repeated closing point removed.</returns>
    /// <exception cref="InvalidDataException">The geometry is malformed.</exception>
    private static List<Vec2[]> ReadExteriorRings(JsonElement geometry)
    {
        string type = geometry.TryGetProperty("type", out JsonElement typeElement)
            ? typeElement.GetString() ?? string.Empty
            : string.Empty;

        if (!geometry.TryGetProperty("coordinates", out JsonElement coordinates))
        {
            throw new InvalidDataException("The geometry has no coordinates.");
        }

        var rings = new List<Vec2[]>();
        switch (type)
        {
            case "Polygon":
                rings.Add(ReadRing(coordinates[0]));
                break;

            case "MultiPolygon":
                foreach (JsonElement polygon in coordinates.EnumerateArray())
                {
                    rings.Add(ReadRing(polygon[0]));
                }

                break;

            default:
                throw new InvalidDataException($"Unsupported geometry type '{type}'.");
        }

        return rings;
    }

    /// <summary>Reads one linear ring as longitude and latitude pairs.</summary>
    /// <param name="ring">The ring element.</param>
    /// <returns>The ring without its repeated closing point.</returns>
    /// <exception cref="InvalidDataException">The ring is too short.</exception>
    private static Vec2[] ReadRing(JsonElement ring)
    {
        int length = ring.GetArrayLength();
        var points = new List<Vec2>(length);
        foreach (JsonElement position in ring.EnumerateArray())
        {
            points.Add(new Vec2(position[0].GetDouble(), position[1].GetDouble()));
        }

        // GeoJSON rings repeat the first position as the last one.
        if (points.Count > 1 && points[0] == points[^1])
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count < 3)
        {
            throw new InvalidDataException("A linear ring needs at least three distinct positions.");
        }

        return [.. points];
    }
}
