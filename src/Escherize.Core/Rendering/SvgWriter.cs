using System.Globalization;
using System.Text;
using Escherize.Geometry;
using Escherize.Preprocessing;

namespace Escherize.Rendering;

/// <summary>
/// Writes SVG output (SPEC §9.1). All numbers are formatted with the invariant culture
/// (SPEC §2). Geometry is y-up, so the y axis is mirrored on the way into SVG.
/// </summary>
public static class SvgWriter
{
    /// <summary>The side of the square canvas in user units.</summary>
    private const double CanvasSize = 800;

    /// <summary>The margin around the drawing, as a fraction of the canvas.</summary>
    private const double MarginFraction = 0.08;

    /// <summary>
    /// Writes the normalised goal outline with a point index label on every tenth point
    /// (SPEC §9.1).
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="goal">The goal shape.</param>
    /// <param name="labelEvery">The label interval; the default is every tenth point.</param>
    public static void WriteGoal(string path, GoalShape goal, int labelEvery = 10)
    {
        ArgumentNullException.ThrowIfNull(goal);
        File.WriteAllText(path, BuildGoal(goal, labelEvery), new UTF8Encoding(false));
    }

    /// <summary>Builds the goal SVG document.</summary>
    /// <param name="goal">The goal shape.</param>
    /// <param name="labelEvery">The label interval.</param>
    /// <returns>The SVG document.</returns>
    public static string BuildGoal(GoalShape goal, int labelEvery = 10)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentOutOfRangeException.ThrowIfLessThan(labelEvery, 1);

        ReadOnlySpan<Vec2> points = goal.Points;
        var view = ViewBox.Fit(points, CanvasSize, MarginFraction);

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <svg xmlns="http://www.w3.org/2000/svg" width="{F(CanvasSize)}" height="{F(CanvasSize)}" viewBox="0 0 {F(CanvasSize)} {F(CanvasSize)}">
              <rect width="100%" height="100%" fill="#ffffff"/>

            """);

        svg.Append("  <path d=\"");
        for (int i = 0; i < points.Length; i++)
        {
            Vec2 p = view.ToCanvas(points[i]);
            svg.Append(CultureInfo.InvariantCulture, $"{(i == 0 ? "M" : "L")}{F(p.X)},{F(p.Y)} ");
        }

        svg.Append("Z\" fill=\"#e8eef5\" stroke=\"#22304a\" stroke-width=\"1.5\" stroke-linejoin=\"round\"/>\n");

        for (int i = 0; i < points.Length; i++)
        {
            Vec2 p = view.ToCanvas(points[i]);
            bool labelled = i % labelEvery == 0;
            double radius = labelled ? 3.2 : 1.8;
            string fill = labelled ? "#c0392b" : "#22304a";
            svg.Append(CultureInfo.InvariantCulture,
                $"  <circle cx=\"{F(p.X)}\" cy=\"{F(p.Y)}\" r=\"{F(radius)}\" fill=\"{fill}\"/>\n");

            if (labelled)
            {
                svg.Append(CultureInfo.InvariantCulture,
                    $"  <text x=\"{F(p.X + 6)}\" y=\"{F(p.Y - 6)}\" font-family=\"sans-serif\" font-size=\"12\" fill=\"#c0392b\">{i}</text>\n");
            }
        }

        svg.Append(CultureInfo.InvariantCulture,
            $"  <text x=\"12\" y=\"24\" font-family=\"sans-serif\" font-size=\"14\" fill=\"#22304a\">n = {points.Length}</text>\n");
        svg.Append("</svg>\n");
        return svg.ToString();
    }

    /// <summary>Formats a number for SVG output with the invariant culture.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The formatted number.</returns>
    internal static string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Maps y-up geometry into the y-down SVG canvas, preserving the aspect ratio.
    /// </summary>
    /// <param name="OffsetX">The horizontal offset in canvas units.</param>
    /// <param name="OffsetY">The vertical offset in canvas units.</param>
    /// <param name="Scale">The uniform scale factor.</param>
    /// <param name="MinX">The smallest x of the source geometry.</param>
    /// <param name="MaxY">The largest y of the source geometry.</param>
    internal readonly record struct ViewBox(double OffsetX, double OffsetY, double Scale, double MinX, double MaxY)
    {
        /// <summary>Fits the given points into a square canvas.</summary>
        /// <param name="points">The points to fit.</param>
        /// <param name="canvasSize">The canvas side.</param>
        /// <param name="marginFraction">The margin as a fraction of the canvas side.</param>
        /// <returns>The view box.</returns>
        public static ViewBox Fit(ReadOnlySpan<Vec2> points, double canvasSize, double marginFraction)
        {
            double minX = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity;
            double maxY = double.NegativeInfinity;

            for (int i = 0; i < points.Length; i++)
            {
                minX = Math.Min(minX, points[i].X);
                maxX = Math.Max(maxX, points[i].X);
                minY = Math.Min(minY, points[i].Y);
                maxY = Math.Max(maxY, points[i].Y);
            }

            double margin = canvasSize * marginFraction;
            double available = canvasSize - (2 * margin);
            double width = Math.Max(maxX - minX, 1e-12);
            double height = Math.Max(maxY - minY, 1e-12);
            double scale = Math.Min(available / width, available / height);

            double offsetX = margin + ((available - (width * scale)) * 0.5);
            double offsetY = margin + ((available - (height * scale)) * 0.5);
            return new ViewBox(offsetX, offsetY, scale, minX, maxY);
        }

        /// <summary>Maps a point into canvas coordinates.</summary>
        /// <param name="point">The point in geometry coordinates.</param>
        /// <returns>The point in canvas coordinates.</returns>
        public Vec2 ToCanvas(Vec2 point) => new(
            OffsetX + ((point.X - MinX) * Scale),
            OffsetY + ((MaxY - point.Y) * Scale));
    }
}
