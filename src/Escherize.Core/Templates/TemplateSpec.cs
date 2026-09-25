namespace Escherize.Templates;

/// <summary>The kind of relation an edge takes part in (SPEC §3, §5.1).</summary>
public enum EdgeKind
{
    /// <summary>An S edge: the edge maps onto itself under a half turn about its midpoint.</summary>
    C,

    /// <summary>One edge of a translation pair.</summary>
    T,

    /// <summary>One edge of a glide pair.</summary>
    G,

    /// <summary>One edge of a rotation pair.</summary>
    R,
}

/// <summary>The axis a glide pair mirrors in (SPEC §5.1).</summary>
public enum GlideAxis
{
    /// <summary>No mirror; used by every edge kind except <see cref="EdgeKind.G"/>.</summary>
    None,

    /// <summary>The matrix F is diag(1, -1), so the x axis is kept fixed.</summary>
    X,

    /// <summary>The matrix F is diag(-1, 1), so the y axis is kept fixed.</summary>
    Y,
}

/// <summary>Helpers for <see cref="GlideAxis"/>.</summary>
public static class GlideAxisExtensions
{
    /// <summary>
    /// The diagonal entries of the mirror matrix F: diag(1, -1) for the x axis and
    /// diag(-1, 1) for the y axis (SPEC §5.1).
    /// </summary>
    /// <param name="axis">The axis kept fixed.</param>
    /// <returns>The two diagonal entries.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The axis is <see cref="GlideAxis.None"/>.</exception>
    public static (double X, double Y) MirrorFactors(this GlideAxis axis) => axis switch
    {
        GlideAxis.X => (1.0, -1.0),
        GlideAxis.Y => (-1.0, 1.0),
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "A glide pair needs an axis."),
    };
}

/// <summary>One edge of a tile template (SPEC §3).</summary>
/// <param name="Kind">The relation the edge takes part in.</param>
/// <param name="PairId">
/// The identifier shared by the two edges of a pair, or -1 for a C edge.
/// </param>
/// <param name="KVar">The index of the k variable that gives the number of interior points.</param>
/// <param name="Axis">The mirror axis, for a glide pair only.</param>
/// <param name="ThetaDeg">The rotation angle in degrees, for a rotation pair only.</param>
public sealed record EdgeSpec(
    EdgeKind Kind,
    int PairId,
    int KVar,
    GlideAxis Axis = GlideAxis.None,
    double ThetaDeg = 0)
{
    /// <summary>The rotation angle in radians.</summary>
    public double ThetaRadians => double.DegreesToRadians(ThetaDeg);
}

/// <summary>
/// One of the nine general isohedral tile templates (SPEC §5.2). The edges are listed in
/// boundary order; edge s runs from vertex s to vertex s + 1.
/// </summary>
/// <param name="Name">The isohedral type name, such as IH4.</param>
/// <param name="Heesch">The Heesch incidence symbol.</param>
/// <param name="Edges">The edges in boundary order.</param>
public sealed record TemplateSpec(string Name, string Heesch, EdgeSpec[] Edges)
{
    /// <summary>
    /// Whether the distance measure has to allow a free rotation, which is the case for
    /// every template that contains a glide pair (SPEC §6.1).
    /// </summary>
    public bool UsesProcrustes
    {
        get
        {
            foreach (EdgeSpec edge in Edges)
            {
                if (edge.Kind == EdgeKind.G)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The number of tiling vertices, nv, which equals the number of edges.</summary>
    public int VertexCount => Edges.Length;

    /// <summary>The number of distinct k variables (SPEC §5.6).</summary>
    public int KVariableCount
    {
        get
        {
            int highest = -1;
            foreach (EdgeSpec edge in Edges)
            {
                highest = Math.Max(highest, edge.KVar);
            }

            return highest + 1;
        }
    }

    /// <summary>
    /// The multiplicity of every k variable: how many edges use it, which is two for a
    /// pair and one for a C edge (SPEC §5.6).
    /// </summary>
    /// <returns>The multiplicities, indexed by k variable.</returns>
    public int[] KMultiplicities()
    {
        var multiplicities = new int[KVariableCount];
        foreach (EdgeSpec edge in Edges)
        {
            multiplicities[edge.KVar]++;
        }

        return multiplicities;
    }

    /// <summary>
    /// The edge pairs, as (a, b) index pairs in boundary order of the first edge. C edges
    /// are not pairs and are left out.
    /// </summary>
    /// <returns>The pairs.</returns>
    public (int A, int B)[] Pairs()
    {
        var pairs = new List<(int, int)>();
        for (int a = 0; a < Edges.Length; a++)
        {
            if (Edges[a].Kind == EdgeKind.C)
            {
                continue;
            }

            for (int b = a + 1; b < Edges.Length; b++)
            {
                if (Edges[b].Kind == Edges[a].Kind && Edges[b].PairId == Edges[a].PairId)
                {
                    pairs.Add((a, b));
                    break;
                }
            }
        }

        return [.. pairs];
    }

    /// <summary>
    /// Returns the template with the glide axes and rotation signs replaced, which is how
    /// the validation of SPEC §8.2 explores the configurations that the specification
    /// deliberately leaves open.
    /// </summary>
    /// <param name="glideAxes">One axis per glide pair, in pair order.</param>
    /// <param name="thetaSign">
    /// The sign every rotation angle is given, +1 or -1. The magnitude is taken from the
    /// template, so the result does not depend on the sign it already carried.
    /// </param>
    /// <returns>The reconfigured template.</returns>
    public TemplateSpec WithConfiguration(ReadOnlySpan<GlideAxis> glideAxes, double thetaSign)
    {
        var glidePairIds = new List<int>();
        foreach (EdgeSpec edge in Edges)
        {
            if (edge.Kind == EdgeKind.G && !glidePairIds.Contains(edge.PairId))
            {
                glidePairIds.Add(edge.PairId);
            }
        }

        if (glideAxes.Length != glidePairIds.Count)
        {
            throw new ArgumentException(
                $"Template {Name} has {glidePairIds.Count} glide pairs, not {glideAxes.Length}.",
                nameof(glideAxes));
        }

        var edges = new EdgeSpec[Edges.Length];
        for (int i = 0; i < Edges.Length; i++)
        {
            EdgeSpec edge = Edges[i];
            edges[i] = edge.Kind switch
            {
                EdgeKind.G => edge with { Axis = glideAxes[glidePairIds.IndexOf(edge.PairId)] },
                EdgeKind.R => edge with { ThetaDeg = Math.CopySign(edge.ThetaDeg, thetaSign) },
                _ => edge,
            };
        }

        return this with { Edges = edges };
    }

    /// <summary>The number of glide pairs, which decides how many axis combinations exist (SPEC §8.2).</summary>
    public int GlidePairCount
    {
        get
        {
            var ids = new List<int>();
            foreach (EdgeSpec edge in Edges)
            {
                if (edge.Kind == EdgeKind.G && !ids.Contains(edge.PairId))
                {
                    ids.Add(edge.PairId);
                }
            }

            return ids.Count;
        }
    }

    /// <summary>Whether the template contains any rotation pair, so that the sign of theta matters.</summary>
    public bool HasRotationPairs
    {
        get
        {
            foreach (EdgeSpec edge in Edges)
            {
                if (edge.Kind == EdgeKind.R)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
