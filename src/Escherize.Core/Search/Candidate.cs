using Escherize.Geometry;
using Escherize.Templates;

namespace Escherize.Search;

/// <summary>
/// Identifies one point of the search space (SPEC §3): a template, a distribution of
/// interior points, a start offset and an orientation.
/// </summary>
/// <param name="TypeIndex">The index of the template in <see cref="TemplateLibrary.All"/>.</param>
/// <param name="K">The k vector.</param>
/// <param name="J">The start offset j.</param>
/// <param name="Reversed">Whether the reversed goal W_rev was used (SPEC §4.5).</param>
public readonly record struct CandidateKey(int TypeIndex, int[] K, int J, bool Reversed);

/// <summary>A key with the distance it achieved (SPEC §3).</summary>
/// <param name="Key">The search key.</param>
/// <param name="Error">The distance measure e of SPEC §6.1.</param>
public sealed record ScoredCandidate(CandidateKey Key, double Error);

/// <summary>
/// A fully reconstructed candidate (SPEC §6.4, §7.3): the tile itself, where its vertices
/// and edges are, how it maps onto the goal, and the quality measures used for filtering.
/// </summary>
public sealed class Candidate
{
    /// <summary>The search key.</summary>
    public required CandidateKey Key { get; init; }

    /// <summary>The template.</summary>
    public required TemplateSpec Template { get; init; }

    /// <summary>The layout for this k vector.</summary>
    public required TileLayout Layout { get; init; }

    /// <summary>The distance measure e (SPEC §6.1).</summary>
    public required double Error { get; init; }

    /// <summary>The weighted distance, when landmarks were given (SPEC §7.4). Null otherwise.</summary>
    public double? WeightedError { get; init; }

    /// <summary>
    /// The tile in normalised coordinates, in template order, aligned to the goal by the
    /// optimal rotation of SPEC §6.4.
    /// </summary>
    public required Vec2[] NormalizedTile { get; init; }

    /// <summary>The tile in input units (SPEC §6.4).</summary>
    public required Vec2[] Tile { get; init; }

    /// <summary>
    /// The same tile points ordered by goal index rather than template order, which is
    /// what the diversity filter compares (SPEC §7.3).
    /// </summary>
    public required Vec2[] GoalOrderedTile { get; init; }

    /// <summary>The point index of each tiling vertex, h(s) (SPEC §3).</summary>
    public required int[] VertexIndices { get; init; }

    /// <summary>The neighbour isometry of every edge (SPEC §5.1), matching <see cref="Tile"/>.</summary>
    public required Isometry[] Isometries { get; init; }

    /// <summary>The optimal overlay rotation in radians (SPEC §6.4).</summary>
    public required double OverlayRotation { get; init; }

    /// <summary>The neck width relative to the square root of the area (SPEC §7.3).</summary>
    public required double RelativeNeckWidth { get; init; }

    /// <summary>The goal index that template index t maps to (SPEC §6.4).</summary>
    /// <param name="t">The template index.</param>
    /// <returns>The goal index.</returns>
    public int GoalIndex(int t)
    {
        int n = Layout.PointCount;
        int shifted = (t + Key.J) % n;
        return Key.Reversed ? (n - shifted) % n : shifted;
    }

    /// <summary>The root of the error as a percentage, the relative RMS deviation (SPEC §6.1).</summary>
    public double RootErrorPercent => 100.0 * Math.Sqrt(Math.Max(0, Error));
}
