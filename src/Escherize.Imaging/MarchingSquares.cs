using Escherize.Geometry;

namespace Escherize.Imaging;

/// <summary>
/// Marching squares contour extraction at iso value 0.5 on a binary mask (SPEC §4.1).
/// </summary>
/// <remarks>
/// The mask is padded with one background pixel on every side so that every loop closes
/// inside the grid. Because the field is binary, the iso crossing always falls exactly on
/// the midpoint of a cell edge, so every contour coordinate is a multiple of one half and
/// endpoints match exactly; loops can therefore be linked without a distance tolerance.
/// The two ambiguous cases are resolved in favour of a connected foreground, which matches
/// the eight connectivity used when the largest component is selected.
/// </remarks>
public static class MarchingSquares
{
    /// <summary>Edge identifiers of a cell.</summary>
    private const int Top = 0;
    private const int Right = 1;
    private const int Bottom = 2;
    private const int Left = 3;

    /// <summary>
    /// The directed segments emitted per case, as pairs of edge identifiers. The case
    /// index is tl*8 + tr*4 + br*2 + bl. A value of -1 marks an unused slot.
    /// </summary>
    private static readonly int[][] CaseTable =
    [
        [],                                     //  0: empty
        [Left, Bottom],                         //  1: bl
        [Bottom, Right],                        //  2: br
        [Left, Right],                          //  3: bl br
        [Right, Top],                           //  4: tr
        [Left, Top, Right, Bottom],             //  5: tr bl, foreground kept connected
        [Bottom, Top],                          //  6: tr br
        [Left, Top],                            //  7: bl br tr
        [Top, Left],                            //  8: tl
        [Top, Bottom],                          //  9: tl bl
        [Top, Right, Bottom, Left],             // 10: tl br, foreground kept connected
        [Top, Right],                           // 11: tl bl br
        [Right, Left],                          // 12: tl tr
        [Right, Bottom],                        // 13: tl tr bl
        [Bottom, Left],                         // 14: tl tr br
        [],                                     // 15: full
    ];

    /// <summary>
    /// Traces all closed contours of the mask and returns the longest one, in pixel
    /// coordinates with y pointing down.
    /// </summary>
    /// <param name="mask">The binary mask.</param>
    /// <returns>The longest closed contour.</returns>
    /// <exception cref="InvalidDataException">The mask produced no closed contour.</exception>
    public static Vec2[] TraceLongestContour(BinaryMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        List<Vec2[]> loops = TraceAllContours(mask);
        if (loops.Count == 0)
        {
            throw new InvalidDataException("The mask produced no closed contour.");
        }

        Vec2[] best = loops[0];
        double bestLength = PolygonOps.Perimeter(best);
        for (int i = 1; i < loops.Count; i++)
        {
            double length = PolygonOps.Perimeter(loops[i]);
            if (length > bestLength)
            {
                bestLength = length;
                best = loops[i];
            }
        }

        return best;
    }

    /// <summary>Traces every closed contour of the mask.</summary>
    /// <param name="mask">The binary mask.</param>
    /// <returns>The closed contours, in pixel coordinates with y pointing down.</returns>
    public static List<Vec2[]> TraceAllContours(BinaryMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        // Half integer coordinates are stored as integers at twice the resolution, so
        // that segment endpoints compare exactly.
        var successor = new Dictionary<long, long>();
        var starts = new List<long>();

        // One cell is spanned by the corners (x, y) and (x + 1, y + 1); the loop runs
        // from -1 so that the virtual background border is included.
        for (int y = -1; y < mask.Height; y++)
        {
            for (int x = -1; x < mask.Width; x++)
            {
                int caseIndex = (mask[x, y] ? 8 : 0)
                    | (mask[x + 1, y] ? 4 : 0)
                    | (mask[x + 1, y + 1] ? 2 : 0)
                    | (mask[x, y + 1] ? 1 : 0);

                int[] segments = CaseTable[caseIndex];
                for (int s = 0; s + 1 < segments.Length; s += 2)
                {
                    long from = EdgeKey(x, y, segments[s]);
                    long to = EdgeKey(x, y, segments[s + 1]);
                    successor[from] = to;
                    starts.Add(from);
                }
            }
        }

        var loops = new List<Vec2[]>();
        var visited = new HashSet<long>();

        foreach (long start in starts)
        {
            if (!visited.Add(start))
            {
                continue;
            }

            var loop = new List<Vec2>();
            long current = start;
            while (true)
            {
                loop.Add(DecodeKey(current));
                if (!successor.TryGetValue(current, out long next))
                {
                    // Cannot happen for a padded mask, but a broken chain is dropped
                    // rather than allowed to produce an open contour.
                    loop.Clear();
                    break;
                }

                if (next == start)
                {
                    break;
                }

                if (!visited.Add(next))
                {
                    loop.Clear();
                    break;
                }

                current = next;
            }

            if (loop.Count >= 3)
            {
                loops.Add([.. loop]);
            }
        }

        return loops;
    }

    /// <summary>Encodes the midpoint of one cell edge at twice the grid resolution.</summary>
    /// <param name="x">The cell column.</param>
    /// <param name="y">The cell row.</param>
    /// <param name="edge">The edge identifier.</param>
    /// <returns>The packed key.</returns>
    private static long EdgeKey(int x, int y, int edge)
    {
        (int doubledX, int doubledY) = edge switch
        {
            Top => ((2 * x) + 1, 2 * y),
            Right => ((2 * x) + 2, (2 * y) + 1),
            Bottom => ((2 * x) + 1, (2 * y) + 2),
            _ => (2 * x, (2 * y) + 1),
        };

        // The offset keeps the packed halves non negative for the padding border.
        return ((long)(doubledX + 4) << 32) | (uint)(doubledY + 4);
    }

    /// <summary>Decodes a packed key back to pixel coordinates.</summary>
    /// <param name="key">The packed key.</param>
    /// <returns>The point in pixel coordinates, y down.</returns>
    private static Vec2 DecodeKey(long key)
    {
        int doubledX = (int)(key >> 32) - 4;
        int doubledY = (int)(key & 0xFFFFFFFF) - 4;
        return new Vec2(doubledX * 0.5, doubledY * 0.5);
    }
}
