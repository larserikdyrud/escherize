using System.Globalization;
using System.Text;
using Escherize.Geometry;

namespace Escherize.Rendering;

/// <summary>
/// Writes the tile outline as ASCII DXF R12 (SPEC §9.4): one closed POLYLINE on the layer
/// TILE, scaled so that the longest side of its bounding box has the requested length in
/// millimetres.
/// </summary>
public static class DxfWriter
{
    /// <summary>The layer the outline is written to (SPEC §9.4).</summary>
    public const string LayerName = "TILE";

    /// <summary>The value of INSUNITS that means millimetres (SPEC §9.4).</summary>
    public const int MillimetreUnits = 4;

    /// <summary>Writes one tile.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="tile">The tile outline.</param>
    /// <param name="sizeMillimetres">The length of the longest bounding box side.</param>
    public static void Write(string path, ReadOnlySpan<Vec2> tile, double sizeMillimetres)
    {
        File.WriteAllText(path, Build(tile, sizeMillimetres), new UTF8Encoding(false));
    }

    /// <summary>Builds the DXF document.</summary>
    /// <param name="tile">The tile outline.</param>
    /// <param name="sizeMillimetres">The length of the longest bounding box side.</param>
    /// <returns>The document text.</returns>
    /// <exception cref="ArgumentException">The outline is empty or degenerate.</exception>
    public static string Build(ReadOnlySpan<Vec2> tile, double sizeMillimetres)
    {
        if (tile.Length < 3)
        {
            throw new ArgumentException("A tile outline needs at least three points.", nameof(tile));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeMillimetres);

        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;
        for (int i = 0; i < tile.Length; i++)
        {
            minX = Math.Min(minX, tile[i].X);
            maxX = Math.Max(maxX, tile[i].X);
            minY = Math.Min(minY, tile[i].Y);
            maxY = Math.Max(maxY, tile[i].Y);
        }

        double extent = Math.Max(maxX - minX, maxY - minY);
        if (extent <= 0)
        {
            throw new ArgumentException("The tile outline has no extent.", nameof(tile));
        }

        double scale = sizeMillimetres / extent;
        double centreX = (minX + maxX) / 2;
        double centreY = (minY + maxY) / 2;

        var dxf = new StringBuilder();

        // Header: only the units variable is needed for R12.
        Pair(dxf, 0, "SECTION");
        Pair(dxf, 2, "HEADER");
        Pair(dxf, 9, "$INSUNITS");
        Pair(dxf, 70, MillimetreUnits.ToString(CultureInfo.InvariantCulture));
        Pair(dxf, 0, "ENDSEC");

        Pair(dxf, 0, "SECTION");
        Pair(dxf, 2, "ENTITIES");

        Pair(dxf, 0, "POLYLINE");
        Pair(dxf, 8, LayerName);
        Pair(dxf, 66, "1");
        Pair(dxf, 70, "1");
        Pair(dxf, 10, "0.0");
        Pair(dxf, 20, "0.0");
        Pair(dxf, 30, "0.0");

        for (int i = 0; i < tile.Length; i++)
        {
            Pair(dxf, 0, "VERTEX");
            Pair(dxf, 8, LayerName);
            Pair(dxf, 10, Number((tile[i].X - centreX) * scale));
            Pair(dxf, 20, Number((tile[i].Y - centreY) * scale));
            Pair(dxf, 30, "0.0");
        }

        Pair(dxf, 0, "SEQEND");
        Pair(dxf, 8, LayerName);

        Pair(dxf, 0, "ENDSEC");
        Pair(dxf, 0, "EOF");
        return dxf.ToString();
    }

    /// <summary>Writes one group code and its value, each on its own line.</summary>
    /// <param name="dxf">The builder.</param>
    /// <param name="code">The group code.</param>
    /// <param name="value">The value.</param>
    private static void Pair(StringBuilder dxf, int code, string value)
    {
        dxf.Append(code.ToString(CultureInfo.InvariantCulture)).Append('\n');
        dxf.Append(value).Append('\n');
    }

    /// <summary>Formats a coordinate with the invariant culture (SPEC §2).</summary>
    /// <param name="value">The value in millimetres.</param>
    /// <returns>The formatted number.</returns>
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
