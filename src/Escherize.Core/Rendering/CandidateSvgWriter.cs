using System.Globalization;
using System.Text;
using Escherize.Geometry;
using Escherize.Preprocessing;
using Escherize.Search;
using Escherize.Templates;

namespace Escherize.Rendering;

/// <summary>
/// Draws candidates: the tile with its edges coloured per pair, a patch of the tiling, and
/// a contact sheet of the whole top list (SPEC §9.1).
/// </summary>
public static class CandidateSvgWriter
{
    private const double CanvasSize = 800;
    private const double MarginFraction = 0.08;

    /// <summary>One colour per edge pair, plus one for C edges.</summary>
    private static readonly string[] PairColors =
        ["#c0392b", "#2c7fb8", "#d68910", "#7a5195", "#ef8a62", "#4d9221"];

    /// <summary>
    /// Draws the tile: edges coloured per pair, tiling vertices as dots, and the goal
    /// dashed in grey behind it (SPEC §9.1).
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="candidate">The candidate.</param>
    /// <param name="goal">The goal, drawn as a reference.</param>
    public static void WriteTile(string path, Candidate candidate, GoalShape goal)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(goal);
        File.WriteAllText(path, BuildTile(candidate, goal), new UTF8Encoding(false));
    }

    /// <summary>Builds the tile drawing.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="goal">The goal.</param>
    /// <returns>The SVG document.</returns>
    public static string BuildTile(Candidate candidate, GoalShape goal)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(goal);

        Vec2[] tile = candidate.NormalizedTile;
        var all = new List<Vec2>(tile.Length * 2);
        all.AddRange(tile);
        foreach (Vec2 point in goal.Points)
        {
            all.Add(point);
        }

        var view = SvgWriter.ViewBox.Fit([.. all], CanvasSize, MarginFraction);
        var svg = new StringBuilder();
        AppendHeader(svg, CanvasSize, CanvasSize);

        // The goal, dashed, behind everything.
        svg.Append("  <path d=\"");
        for (int i = 0; i < goal.PointCount; i++)
        {
            Vec2 p = view.ToCanvas(goal.Points[i]);
            svg.Append(CultureInfo.InvariantCulture, $"{(i == 0 ? "M" : "L")}{SvgWriter.F(p.X)},{SvgWriter.F(p.Y)} ");
        }

        svg.Append("Z\" fill=\"none\" stroke=\"#9aa4b1\" stroke-width=\"1.4\" stroke-dasharray=\"6 5\"/>\n");

        // The tile body.
        svg.Append("  <path d=\"");
        for (int i = 0; i < tile.Length; i++)
        {
            Vec2 p = view.ToCanvas(tile[i]);
            svg.Append(CultureInfo.InvariantCulture, $"{(i == 0 ? "M" : "L")}{SvgWriter.F(p.X)},{SvgWriter.F(p.Y)} ");
        }

        svg.Append("Z\" fill=\"#f3f5f8\" stroke=\"none\"/>\n");

        // One coloured polyline per tiling edge.
        TileLayout layout = candidate.Layout;
        for (int s = 0; s < layout.VertexCount; s++)
        {
            EdgeSpec edge = candidate.Template.Edges[s];
            string color = ColorFor(edge);

            svg.Append("  <path d=\"");
            for (int i = 0; i <= layout.EdgeK(s) + 1; i++)
            {
                Vec2 p = view.ToCanvas(tile[layout.EdgePointIndex(s, i)]);
                svg.Append(CultureInfo.InvariantCulture,
                    $"{(i == 0 ? "M" : "L")}{SvgWriter.F(p.X)},{SvgWriter.F(p.Y)} ");
            }

            svg.Append(CultureInfo.InvariantCulture,
                $"\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2.4\" stroke-linecap=\"round\"/>\n");
        }

        // The tiling vertices.
        foreach (int index in candidate.VertexIndices)
        {
            Vec2 p = view.ToCanvas(tile[index]);
            svg.Append(CultureInfo.InvariantCulture,
                $"  <circle cx=\"{SvgWriter.F(p.X)}\" cy=\"{SvgWriter.F(p.Y)}\" r=\"4.5\" " +
                $"fill=\"#ffffff\" stroke=\"#22304a\" stroke-width=\"1.8\"/>\n");
        }

        AppendCaption(svg, candidate);
        svg.Append("</svg>\n");
        return svg.ToString();
    }

    /// <summary>Draws a patch of the tiling, clipped to the canvas (SPEC §9.1, §9.3).</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="candidate">The candidate.</param>
    /// <param name="tileCount">How many tiles to place, between 30 and 80.</param>
    public static void WriteTiling(string path, Candidate candidate, int tileCount = 60)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        File.WriteAllText(path, BuildTiling(candidate, tileCount), new UTF8Encoding(false));
    }

    /// <summary>Builds the tiling drawing.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="tileCount">How many tiles to place.</param>
    /// <returns>The SVG document.</returns>
    public static string BuildTiling(Candidate candidate, int tileCount = 60)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        List<ColoredTile> patch = TilingColorer.Build(
            candidate.Layout,
            candidate.NormalizedTile,
            candidate.Isometries,
            Math.Clamp(tileCount, 30, 80));

        var all = new List<Vec2>();
        foreach (ColoredTile placed in patch)
        {
            all.AddRange(placed.Points);
        }

        var view = SvgWriter.ViewBox.Fit([.. all], CanvasSize, MarginFraction);
        var svg = new StringBuilder();
        AppendHeader(svg, CanvasSize, CanvasSize);

        // The patch is clipped to a rectangle so the ragged outer boundary is hidden.
        double inset = CanvasSize * 0.06;
        svg.Append(CultureInfo.InvariantCulture, $"""
              <clipPath id="frame">
                <rect x="{SvgWriter.F(inset)}" y="{SvgWriter.F(inset)}" width="{SvgWriter.F(CanvasSize - (2 * inset))}" height="{SvgWriter.F(CanvasSize - (2 * inset))}"/>
              </clipPath>
              <g clip-path="url(#frame)">

            """);

        foreach (ColoredTile placed in patch)
        {
            svg.Append("    <path d=\"");
            for (int i = 0; i < placed.Points.Length; i++)
            {
                Vec2 p = view.ToCanvas(placed.Points[i]);
                svg.Append(CultureInfo.InvariantCulture,
                    $"{(i == 0 ? "M" : "L")}{SvgWriter.F(p.X)},{SvgWriter.F(p.Y)} ");
            }

            svg.Append(CultureInfo.InvariantCulture,
                $"Z\" fill=\"{TilingColorer.Palette[placed.ColorIndex]}\" stroke=\"#3d4550\" stroke-width=\"1\"/>\n");
        }

        svg.Append("  </g>\n");
        svg.Append(CultureInfo.InvariantCulture, $"""
              <rect x="{SvgWriter.F(inset)}" y="{SvgWriter.F(inset)}" width="{SvgWriter.F(CanvasSize - (2 * inset))}" height="{SvgWriter.F(CanvasSize - (2 * inset))}" fill="none" stroke="#22304a" stroke-width="1.5"/>

            """);

        AppendCaption(svg, candidate);
        svg.Append("</svg>\n");
        return svg.ToString();
    }

    /// <summary>Draws the grid of the whole top list (SPEC §9.1).</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="candidates">The candidates, best first.</param>
    public static void WriteContactSheet(string path, IReadOnlyList<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        File.WriteAllText(path, BuildContactSheet(candidates), new UTF8Encoding(false));
    }

    /// <summary>Builds the contact sheet.</summary>
    /// <param name="candidates">The candidates, best first.</param>
    /// <returns>The SVG document.</returns>
    public static string BuildContactSheet(IReadOnlyList<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        const double Cell = 220;
        const double LabelHeight = 34;
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, candidates.Count))));
        int rows = Math.Max(1, (int)Math.Ceiling(candidates.Count / (double)columns));

        double width = columns * Cell;
        double height = rows * (Cell + LabelHeight);

        var svg = new StringBuilder();
        AppendHeader(svg, width, height);

        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate candidate = candidates[i];
            double originX = (i % columns) * Cell;
            double originY = (i / columns) * (Cell + LabelHeight);

            var view = SvgWriter.ViewBox.Fit(candidate.NormalizedTile, Cell, 0.14);

            svg.Append(CultureInfo.InvariantCulture,
                $"  <g transform=\"translate({SvgWriter.F(originX)},{SvgWriter.F(originY)})\">\n");
            svg.Append("    <path d=\"");
            for (int t = 0; t < candidate.NormalizedTile.Length; t++)
            {
                Vec2 p = view.ToCanvas(candidate.NormalizedTile[t]);
                svg.Append(CultureInfo.InvariantCulture,
                    $"{(t == 0 ? "M" : "L")}{SvgWriter.F(p.X)},{SvgWriter.F(p.Y)} ");
            }

            svg.Append("Z\" fill=\"#dde5ee\" stroke=\"#22304a\" stroke-width=\"1.3\"/>\n");

            string neck = candidate.RelativeNeckWidth.ToString("0.###", CultureInfo.InvariantCulture);
            string rms = candidate.RootErrorPercent.ToString("0.##", CultureInfo.InvariantCulture);
            svg.Append(CultureInfo.InvariantCulture,
                $"    <text x=\"8\" y=\"{SvgWriter.F(Cell + 14)}\" font-family=\"sans-serif\" font-size=\"13\" fill=\"#22304a\">" +
                $"#{i + 1} {candidate.Template.Name}</text>\n");
            svg.Append(CultureInfo.InvariantCulture,
                $"    <text x=\"8\" y=\"{SvgWriter.F(Cell + 29)}\" font-family=\"sans-serif\" font-size=\"11\" fill=\"#5a6673\">" +
                $"rms {rms} %, neck {neck}</text>\n");
            svg.Append("  </g>\n");
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    /// <summary>Writes the SVG prologue and a white background.</summary>
    /// <param name="svg">The builder.</param>
    /// <param name="width">The canvas width.</param>
    /// <param name="height">The canvas height.</param>
    private static void AppendHeader(StringBuilder svg, double width, double height) =>
        svg.Append(CultureInfo.InvariantCulture, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <svg xmlns="http://www.w3.org/2000/svg" width="{SvgWriter.F(width)}" height="{SvgWriter.F(height)}" viewBox="0 0 {SvgWriter.F(width)} {SvgWriter.F(height)}">
              <rect width="100%" height="100%" fill="#ffffff"/>

            """);

    /// <summary>Writes the caption line shared by the tile and tiling drawings.</summary>
    /// <param name="svg">The builder.</param>
    /// <param name="candidate">The candidate.</param>
    private static void AppendCaption(StringBuilder svg, Candidate candidate)
    {
        string rms = candidate.RootErrorPercent.ToString("0.##", CultureInfo.InvariantCulture);
        string neck = candidate.RelativeNeckWidth.ToString("0.###", CultureInfo.InvariantCulture);
        string k = string.Join(",", candidate.Key.K);
        svg.Append(CultureInfo.InvariantCulture,
            $"  <text x=\"14\" y=\"26\" font-family=\"sans-serif\" font-size=\"15\" fill=\"#22304a\">" +
            $"{candidate.Template.Name} {candidate.Template.Heesch}  k=[{k}]  j={candidate.Key.J}" +
            $"{(candidate.Key.Reversed ? "  reversed" : string.Empty)}</text>\n");
        svg.Append(CultureInfo.InvariantCulture,
            $"  <text x=\"14\" y=\"46\" font-family=\"sans-serif\" font-size=\"13\" fill=\"#5a6673\">" +
            $"rms {rms} %, neck {neck}</text>\n");
    }

    /// <summary>The colour used for an edge, by pair (SPEC §9.1).</summary>
    /// <param name="edge">The edge.</param>
    /// <returns>The colour.</returns>
    private static string ColorFor(EdgeSpec edge) =>
        edge.Kind == EdgeKind.C ? "#5a6673" : PairColors[Math.Clamp(edge.PairId, 0, PairColors.Length - 1)];
}
