using Escherize.Geometry;

namespace Escherize.Rendering;

/// <summary>
/// Writes a tile as a solid in binary STL, so that it can be printed. The outline is
/// triangulated and extruded to the requested height; the result is watertight, which is
/// what a slicer needs.
/// </summary>
/// <remarks>
/// The tile is scaled the same way as the DXF export, so that the longest side of its
/// bounding box has the requested length in millimetres, and is centred on the origin in x
/// and y with its base on z = 0. Binary STL carries no units; every slicer in common use
/// reads it as millimetres, which is what the numbers written here are.
/// </remarks>
public static class StlWriter
{
    /// <summary>Writes the tile as a solid.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="tile">The tile outline.</param>
    /// <param name="sizeMillimetres">The length of the longest bounding box side.</param>
    /// <param name="heightMillimetres">The thickness of the solid.</param>
    public static void Write(string path, ReadOnlySpan<Vec2> tile, double sizeMillimetres, double heightMillimetres)
    {
        byte[] content = Build(tile, sizeMillimetres, heightMillimetres);
        File.WriteAllBytes(path, content);
    }

    /// <summary>Builds the binary STL content.</summary>
    /// <param name="tile">The tile outline.</param>
    /// <param name="sizeMillimetres">The length of the longest bounding box side.</param>
    /// <param name="heightMillimetres">The thickness of the solid.</param>
    /// <returns>The file content.</returns>
    /// <exception cref="ArgumentException">The outline is degenerate or could not be triangulated.</exception>
    public static byte[] Build(ReadOnlySpan<Vec2> tile, double sizeMillimetres, double heightMillimetres)
    {
        if (tile.Length < 3)
        {
            throw new ArgumentException("A tile outline needs at least three points.", nameof(tile));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeMillimetres);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightMillimetres);

        Vec2[] scaled = ScaleAndCentre(tile, sizeMillimetres);
        int[] triangles = Triangulate(scaled);

        // Two faces of the triangulation, plus two triangles per side wall.
        int faceCount = ((triangles.Length / 3) * 2) + (scaled.Length * 2);

        using var stream = new MemoryStream(84 + (faceCount * 50));
        using var writer = new BinaryWriter(stream);

        // An eighty byte header, which by convention must not start with the word solid.
        var header = new byte[80];
        ReadOnlySpan<byte> label = "Escherize tile"u8;
        label.CopyTo(header);
        writer.Write(header);
        writer.Write((uint)faceCount);

        double h = heightMillimetres;

        // The top face, at the full height, keeps the counter-clockwise winding of the
        // outline, so its normal points up.
        for (int i = 0; i < triangles.Length; i += 3)
        {
            WriteTriangle(
                writer,
                new Vec3(scaled[triangles[i]], h),
                new Vec3(scaled[triangles[i + 1]], h),
                new Vec3(scaled[triangles[i + 2]], h));
        }

        // The base is the same triangulation, wound the other way, so its normal points
        // down.
        for (int i = 0; i < triangles.Length; i += 3)
        {
            WriteTriangle(
                writer,
                new Vec3(scaled[triangles[i + 2]], 0),
                new Vec3(scaled[triangles[i + 1]], 0),
                new Vec3(scaled[triangles[i]], 0));
        }

        // One wall per edge, as two triangles, wound so that the normal points away from
        // the interior.
        for (int i = 0; i < scaled.Length; i++)
        {
            Vec2 a = scaled[i];
            Vec2 b = scaled[(i + 1) % scaled.Length];

            WriteTriangle(writer, new Vec3(a, 0), new Vec3(b, 0), new Vec3(b, h));
            WriteTriangle(writer, new Vec3(a, 0), new Vec3(b, h), new Vec3(a, h));
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Scales the outline so that the longest side of its bounding box has the requested
    /// length, and centres it on the origin.
    /// </summary>
    /// <param name="tile">The outline.</param>
    /// <param name="sizeMillimetres">The length of the longest bounding box side.</param>
    /// <returns>The scaled outline.</returns>
    /// <exception cref="ArgumentException">The outline has no extent.</exception>
    private static Vec2[] ScaleAndCentre(ReadOnlySpan<Vec2> tile, double sizeMillimetres)
    {
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

        var result = new Vec2[tile.Length];
        for (int i = 0; i < tile.Length; i++)
        {
            result[i] = new Vec2((tile[i].X - centreX) * scale, (tile[i].Y - centreY) * scale);
        }

        // The walls assume a counter-clockwise outline, which is what the search produces,
        // but a caller could pass anything.
        return PolygonOps.SignedArea(result) >= 0 ? result : PolygonOps.Reverse(result);
    }

    /// <summary>
    /// Triangulates a simple polygon by ear clipping. A tile is simple by construction,
    /// having passed the self intersection filter of SPEC §7.3, but it can be strongly
    /// concave, so a fan would not do.
    /// </summary>
    /// <param name="polygon">The polygon, counter-clockwise.</param>
    /// <returns>Three vertex indices per triangle.</returns>
    /// <exception cref="ArgumentException">No ear could be found, so the polygon is not simple.</exception>
    public static int[] Triangulate(ReadOnlySpan<Vec2> polygon)
    {
        int n = polygon.Length;
        var remaining = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            remaining.Add(i);
        }

        var triangles = new List<int>(3 * Math.Max(0, n - 2));
        int guard = 0;

        while (remaining.Count > 3)
        {
            bool clipped = false;

            for (int i = 0; i < remaining.Count; i++)
            {
                int previous = remaining[(i - 1 + remaining.Count) % remaining.Count];
                int current = remaining[i];
                int next = remaining[(i + 1) % remaining.Count];

                if (!IsEar(polygon, remaining, previous, current, next))
                {
                    continue;
                }

                triangles.Add(previous);
                triangles.Add(current);
                triangles.Add(next);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped || ++guard > n)
            {
                throw new ArgumentException(
                    "The outline could not be triangulated; it is probably not a simple polygon.",
                    nameof(polygon));
            }
        }

        triangles.Add(remaining[0]);
        triangles.Add(remaining[1]);
        triangles.Add(remaining[2]);
        return [.. triangles];
    }

    /// <summary>
    /// Whether the corner at <paramref name="current"/> can be clipped: it must turn left,
    /// and no other remaining vertex may lie inside the triangle it would cut off.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="remaining">The vertices not yet clipped.</param>
    /// <param name="previous">The vertex before the corner.</param>
    /// <param name="current">The corner.</param>
    /// <param name="next">The vertex after the corner.</param>
    /// <returns>True when the corner is an ear.</returns>
    private static bool IsEar(ReadOnlySpan<Vec2> polygon, List<int> remaining, int previous, int current, int next)
    {
        Vec2 a = polygon[previous];
        Vec2 b = polygon[current];
        Vec2 c = polygon[next];

        // A counter-clockwise polygon turns left at a convex corner.
        double cross = (b - a).Cross(c - a);
        if (cross <= 0)
        {
            return false;
        }

        foreach (int index in remaining)
        {
            if (index == previous || index == current || index == next)
            {
                continue;
            }

            if (InsideTriangle(polygon[index], a, b, c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a point lies inside a counter-clockwise triangle.</summary>
    /// <param name="point">The point.</param>
    /// <param name="a">The first corner.</param>
    /// <param name="b">The second corner.</param>
    /// <param name="c">The third corner.</param>
    /// <returns>True when the point is inside or on the boundary.</returns>
    private static bool InsideTriangle(Vec2 point, Vec2 a, Vec2 b, Vec2 c) =>
        (b - a).Cross(point - a) >= 0
        && (c - b).Cross(point - b) >= 0
        && (a - c).Cross(point - c) >= 0;

    /// <summary>Writes one triangle with the normal implied by its winding.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="a">The first corner.</param>
    /// <param name="b">The second corner.</param>
    /// <param name="c">The third corner.</param>
    private static void WriteTriangle(BinaryWriter writer, Vec3 a, Vec3 b, Vec3 c)
    {
        Vec3 u = b - a;
        Vec3 v = c - a;
        Vec3 normal = new(
            (u.Y * v.Z) - (u.Z * v.Y),
            (u.Z * v.X) - (u.X * v.Z),
            (u.X * v.Y) - (u.Y * v.X));

        double length = Math.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));
        if (length > 0)
        {
            normal = new Vec3(normal.X / length, normal.Y / length, normal.Z / length);
        }

        Write(writer, normal);
        Write(writer, a);
        Write(writer, b);
        Write(writer, c);
        writer.Write((ushort)0);
    }

    /// <summary>Writes one vector as three single precision values.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="value">The vector.</param>
    private static void Write(BinaryWriter writer, Vec3 value)
    {
        writer.Write((float)value.X);
        writer.Write((float)value.Y);
        writer.Write((float)value.Z);
    }

    /// <summary>A point in space, used only while writing the solid.</summary>
    /// <param name="X">The x coordinate.</param>
    /// <param name="Y">The y coordinate.</param>
    /// <param name="Z">The z coordinate.</param>
    private readonly record struct Vec3(double X, double Y, double Z)
    {
        /// <summary>Lifts a planar point to a given height.</summary>
        /// <param name="point">The planar point.</param>
        /// <param name="z">The height.</param>
        public Vec3(Vec2 point, double z)
            : this(point.X, point.Y, z)
        {
        }

        /// <summary>Subtracts two points.</summary>
        /// <param name="a">The first point.</param>
        /// <param name="b">The second point.</param>
        /// <returns>The difference.</returns>
        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }
}
