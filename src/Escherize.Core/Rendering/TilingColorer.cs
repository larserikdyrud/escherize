using Escherize.Geometry;
using Escherize.Templates;

namespace Escherize.Rendering;

/// <summary>A tile of a rendered patch, with the colour it was given.</summary>
/// <param name="Points">The tile outline.</param>
/// <param name="ColorIndex">The index into the palette.</param>
public sealed record ColoredTile(Vec2[] Points, int ColorIndex);

/// <summary>
/// Builds a patch that fills a target rectangle and colours it so that neighbours differ
/// (SPEC §9.3).
/// </summary>
public static class TilingColorer
{
    /// <summary>The six muted colours of the palette (SPEC §9.3).</summary>
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#b8c9d9", "#d9c4b0", "#c2cfbc", "#d6c0c9", "#bfc3d6", "#cfd2c4",
    ];

    /// <summary>The upper bound on the number of tiles (SPEC §9.3).</summary>
    public const int MaximumTiles = 400;

    /// <summary>
    /// Grows a patch until it covers the target rectangle, then colours it greedily in
    /// breadth first order (SPEC §9.3).
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="tile">The base tile.</param>
    /// <param name="isometries">The neighbour isometries, matching the tile.</param>
    /// <param name="targetTileCount">How many tiles the drawing should hold.</param>
    /// <returns>The coloured tiles, the base tile first.</returns>
    public static List<ColoredTile> Build(
        TileLayout layout,
        Vec2[] tile,
        Isometry[] isometries,
        int targetTileCount = 60)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(isometries);

        int limit = Math.Clamp(targetTileCount, 1, MaximumTiles);
        List<PlacedTile> patch = GrowPatch(tile, isometries, limit);

        // Two tiles are neighbours when they share an edge, which shows up as a shared
        // pair of points; comparing quantised endpoints is enough and is exact here
        // because both come from the same arithmetic.
        var adjacency = new List<List<int>>(patch.Count);
        var edgeOwners = new Dictionary<long, List<int>>();
        double quantum = 1e-6 * Math.Max(PolygonOps.Diameter(tile), 1e-12);

        for (int i = 0; i < patch.Count; i++)
        {
            adjacency.Add([]);
            foreach (long key in EdgeKeys(patch[i].Points, layout, quantum))
            {
                if (!edgeOwners.TryGetValue(key, out List<int>? owners))
                {
                    owners = [];
                    edgeOwners[key] = owners;
                }

                owners.Add(i);
            }
        }

        foreach (List<int> owners in edgeOwners.Values)
        {
            for (int a = 0; a < owners.Count; a++)
            {
                for (int b = a + 1; b < owners.Count; b++)
                {
                    adjacency[owners[a]].Add(owners[b]);
                    adjacency[owners[b]].Add(owners[a]);
                }
            }
        }

        var colors = new int[patch.Count];
        Array.Fill(colors, -1);
        var used = new bool[Palette.Count];

        for (int i = 0; i < patch.Count; i++)
        {
            Array.Clear(used);
            foreach (int neighbour in adjacency[i])
            {
                if (colors[neighbour] >= 0)
                {
                    used[colors[neighbour]] = true;
                }
            }

            int chosen = 0;
            for (int c = 0; c < Palette.Count; c++)
            {
                if (!used[c])
                {
                    chosen = c;
                    break;
                }
            }

            colors[i] = chosen;
        }

        var result = new List<ColoredTile>(patch.Count);
        for (int i = 0; i < patch.Count; i++)
        {
            result.Add(new ColoredTile(patch[i].Points, colors[i]));
        }

        return result;
    }

    /// <summary>
    /// Grows the patch breadth first, preferring tiles near the base tile so that the
    /// drawing stays compact (SPEC §9.3).
    /// </summary>
    /// <param name="tile">The base tile.</param>
    /// <param name="isometries">The neighbour isometries.</param>
    /// <param name="limit">The number of tiles to place.</param>
    /// <returns>The placed tiles.</returns>
    private static List<PlacedTile> GrowPatch(Vec2[] tile, Isometry[] isometries, int limit)
    {
        double quantum = 1e-6 * Math.Max(PolygonOps.Diameter(tile), 1e-12);
        var placed = new List<PlacedTile>(limit);
        var seen = new HashSet<(long, long)>();
        var queue = new Queue<PlacedTile>();

        var root = new PlacedTile(Isometry.Identity, tile, 0);
        placed.Add(root);
        seen.Add(CentroidKey(root.Points, quantum));
        queue.Enqueue(root);

        while (queue.Count > 0 && placed.Count < limit)
        {
            PlacedTile current = queue.Dequeue();
            for (int edge = 0; edge < isometries.Length && placed.Count < limit; edge++)
            {
                Isometry transform = isometries[edge].Then(current.Transform);
                var points = new Vec2[tile.Length];
                for (int i = 0; i < tile.Length; i++)
                {
                    points[i] = transform.Apply(tile[i]);
                }

                if (!seen.Add(CentroidKey(points, quantum)))
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

    /// <summary>The quantised centroid of a tile, used to recognise a repeat.</summary>
    /// <param name="points">The tile points.</param>
    /// <param name="quantum">The quantisation step.</param>
    /// <returns>The key.</returns>
    private static (long, long) CentroidKey(ReadOnlySpan<Vec2> points, double quantum)
    {
        Vec2 centroid = PolygonOps.PointAverage(points);
        return ((long)Math.Round(centroid.X / quantum), (long)Math.Round(centroid.Y / quantum));
    }

    /// <summary>The quantised midpoints of the tiling edges of a placed tile.</summary>
    /// <param name="points">The tile points.</param>
    /// <param name="layout">The layout, which says where the tiling vertices are.</param>
    /// <param name="quantum">The quantisation step.</param>
    /// <returns>One key per tiling edge.</returns>
    private static IEnumerable<long> EdgeKeys(Vec2[] points, TileLayout layout, double quantum)
    {
        for (int s = 0; s < layout.VertexCount; s++)
        {
            Vec2 a = points[layout.EdgePointIndex(s, 0)];
            Vec2 b = points[layout.EdgePointIndex(s, layout.EdgeK(s) + 1)];
            Vec2 middle = (a + b) * 0.5;
            long x = (long)Math.Round(middle.X / quantum);
            long y = (long)Math.Round(middle.Y / quantum);
            yield return (x << 21) ^ y;
        }
    }
}
