using System.Globalization;
using Escherize.Geometry;

namespace Escherize.Templates;

/// <summary>One placed copy of the tile.</summary>
/// <param name="Transform">The map from the base tile to this copy.</param>
/// <param name="Points">The transformed tile points.</param>
/// <param name="Depth">The number of edge crossings from the base tile.</param>
public sealed record PlacedTile(Isometry Transform, Vec2[] Points, int Depth)
{
    /// <summary>The smallest x of the tile.</summary>
    public double MinX { get; } = Min(Points, static p => p.X);

    /// <summary>The largest x of the tile.</summary>
    public double MaxX { get; } = Max(Points, static p => p.X);

    /// <summary>The smallest y of the tile.</summary>
    public double MinY { get; } = Min(Points, static p => p.Y);

    /// <summary>The largest y of the tile.</summary>
    public double MaxY { get; } = Max(Points, static p => p.Y);

    /// <summary>Whether the axis aligned box of the tile, grown by a margin, holds a point.</summary>
    /// <param name="point">The point.</param>
    /// <param name="margin">The margin the box is grown by.</param>
    /// <returns>True when the point could be inside or near the tile.</returns>
    public bool BoxContains(Vec2 point, double margin) =>
        point.X >= MinX - margin && point.X <= MaxX + margin &&
        point.Y >= MinY - margin && point.Y <= MaxY + margin;

    private static double Min(Vec2[] points, Func<Vec2, double> selector)
    {
        double best = double.PositiveInfinity;
        foreach (Vec2 point in points)
        {
            best = Math.Min(best, selector(point));
        }

        return best;
    }

    private static double Max(Vec2[] points, Func<Vec2, double> selector)
    {
        double best = double.NegativeInfinity;
        foreach (Vec2 point in points)
        {
            best = Math.Max(best, selector(point));
        }

        return best;
    }
}

/// <summary>
/// Grows a patch of the tiling by breadth first search over the neighbour isometries,
/// starting from the identity (SPEC §9.3).
/// </summary>
public static class TilingPatch
{
    /// <summary>The relative tolerance used when deduplicating on the tile centroid (SPEC §9.3).</summary>
    public const double CentroidToleranceFraction = 1e-6;

    /// <summary>Grows a patch to a given depth.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="tile">The base tile points.</param>
    /// <param name="depth">The number of edge crossings to follow.</param>
    /// <param name="maximumTiles">The upper bound on the number of tiles (SPEC §9.3).</param>
    /// <returns>The placed tiles, the base tile first.</returns>
    public static List<PlacedTile> Grow(TileLayout layout, Vec2[] tile, int depth, int maximumTiles = 400)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);

        double quantum = CentroidToleranceFraction * Math.Max(PolygonOps.Diameter(tile), 1e-12);
        Isometry[] isometries = TileIsometries.Build(layout, tile);

        var placed = new List<PlacedTile>(Math.Min(maximumTiles, 64));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<PlacedTile>();

        var root = new PlacedTile(Isometry.Identity, tile, 0);
        placed.Add(root);
        seen.Add(CentroidKey(root.Points, quantum));
        queue.Enqueue(root);

        while (queue.Count > 0 && placed.Count < maximumTiles)
        {
            PlacedTile current = queue.Dequeue();
            if (current.Depth >= depth)
            {
                continue;
            }

            for (int edge = 0; edge < isometries.Length && placed.Count < maximumTiles; edge++)
            {
                // The neighbour across edge e of the tile at M sits at M composed with g_e.
                Isometry transform = isometries[edge].Then(current.Transform);
                var points = new Vec2[tile.Length];
                for (int i = 0; i < tile.Length; i++)
                {
                    points[i] = transform.Apply(tile[i]);
                }

                string key = CentroidKey(points, quantum);
                if (!seen.Add(key))
                {
                    continue;
                }

                var neighbour = new PlacedTile(transform, points, current.Depth + 1);
                placed.Add(neighbour);
                queue.Enqueue(neighbour);
            }
        }

        return placed;
    }

    /// <summary>The deduplication key: the tile centroid, quantised (SPEC §9.3).</summary>
    /// <param name="points">The tile points.</param>
    /// <param name="quantum">The quantisation step.</param>
    /// <returns>The key.</returns>
    private static string CentroidKey(ReadOnlySpan<Vec2> points, double quantum)
    {
        Vec2 centroid = PolygonOps.PointAverage(points);
        long x = (long)Math.Round(centroid.X / quantum);
        long y = (long)Math.Round(centroid.Y / quantum);
        return string.Create(CultureInfo.InvariantCulture, $"{x}:{y}");
    }
}
