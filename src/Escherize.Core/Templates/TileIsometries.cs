using Escherize.Geometry;

namespace Escherize.Templates;

/// <summary>
/// The neighbour isometries of a concrete tile (SPEC §5.1, right hand column). The map
/// g_e carries the partner edge of e onto e, so the tile placed across edge e of a tile
/// at M sits at M composed with g_e (SPEC §9.3).
/// </summary>
public static class TileIsometries
{
    /// <summary>Builds one neighbour isometry per edge.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="tile">The tile points, in boundary order.</param>
    /// <returns>The isometries, indexed by edge.</returns>
    /// <exception cref="ArgumentException">The tile does not match the layout.</exception>
    public static Isometry[] Build(TileLayout layout, ReadOnlySpan<Vec2> tile)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (tile.Length != layout.PointCount)
        {
            throw new ArgumentException(
                $"Expected {layout.PointCount} tile points, got {tile.Length}.", nameof(tile));
        }

        TemplateSpec template = layout.Template;
        int nv = layout.VertexCount;
        var isometries = new Isometry[nv];

        for (int s = 0; s < nv; s++)
        {
            if (template.Edges[s].Kind != EdgeKind.C)
            {
                continue;
            }

            // g(x) = V_s + V_(s+1) - x, a half turn about the edge midpoint.
            Vec2 vs = tile[layout.EdgePointIndex(s, 0)];
            Vec2 vs1 = tile[layout.EdgePointIndex(s, layout.EdgeK(s) + 1)];
            isometries[s] = Isometry.HalfTurn((vs + vs1) * 0.5);
        }

        foreach ((int a, int b) in template.Pairs())
        {
            EdgeSpec edge = template.Edges[a];
            int ka = layout.EdgeK(a);

            Isometry forward = edge.Kind switch
            {
                // g(x) = x + a(k+1) - b(0)
                EdgeKind.T => Isometry.Translation(
                    tile[layout.EdgePointIndex(a, ka + 1)] - tile[layout.EdgePointIndex(b, 0)]),

                // g(x) = a(0) + F (x - b(0))
                EdgeKind.G => Isometry.Mirror(
                    edge.Axis.MirrorFactors(),
                    tile[layout.EdgePointIndex(b, 0)],
                    tile[layout.EdgePointIndex(a, 0)]),

                // g(x) = V + R(-theta) (x - V), with V = a(k+1) = b(0)
                EdgeKind.R => Isometry.Rotation(
                    tile[layout.EdgePointIndex(b, 0)],
                    -edge.ThetaRadians),

                _ => throw new InvalidOperationException($"Edge kind {edge.Kind} cannot be a pair."),
            };

            isometries[a] = forward;
            isometries[b] = forward.Inverse();
        }

        return isometries;
    }

    /// <summary>
    /// The partner edge of every edge: the edge whose points g_e maps onto edge e. A C
    /// edge is its own partner.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <returns>The partner edge indices.</returns>
    public static int[] PartnerEdges(TemplateSpec template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var partners = new int[template.VertexCount];
        for (int s = 0; s < partners.Length; s++)
        {
            partners[s] = s;
        }

        foreach ((int a, int b) in template.Pairs())
        {
            partners[a] = b;
            partners[b] = a;
        }

        return partners;
    }
}
