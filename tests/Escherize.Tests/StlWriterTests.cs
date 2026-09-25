using Escherize.Geometry;
using Escherize.Rendering;
using Xunit;

namespace Escherize.Tests;

/// <summary>Tests for the printable solid.</summary>
public sealed class StlWriterTests
{
    /// <summary>The header of a binary STL, before the triangle count.</summary>
    private const int HeaderBytes = 80;

    /// <summary>The size of one triangle record.</summary>
    private const int TriangleBytes = 50;

    /// <summary>The file has the size its triangle count implies.</summary>
    [Fact]
    public void FileLayoutIsWellFormed()
    {
        byte[] stl = StlWriter.Build(TestFixtures.Star(), 200, 10);

        long count = BitConverter.ToUInt32(stl, HeaderBytes);
        Assert.Equal(HeaderBytes + 4 + (count * TriangleBytes), stl.Length);

        // A binary STL must not start with the word that marks an ASCII one.
        Assert.NotEqual("solid"u8.ToArray(), stl[..5]);
    }

    /// <summary>A shape of n points gives 2n - 4 face triangles and 2n wall triangles.</summary>
    /// <param name="fixtureName">The fixture.</param>
    [Theory]
    [InlineData("star")]
    [InlineData("lshape")]
    [InlineData("narrowwaist")]
    [InlineData("cat")]
    public void TriangleCountMatchesTheOutline(string fixtureName)
    {
        Vec2[] shape = TestFixtures.Build(fixtureName);
        byte[] stl = StlWriter.Build(shape, 200, 10);

        int n = shape.Length;
        uint count = BitConverter.ToUInt32(stl, HeaderBytes);
        Assert.Equal((uint)((2 * (n - 2)) + (2 * n)), count);
    }

    /// <summary>
    /// The solid is watertight: every edge is shared by exactly two triangles, which is
    /// what a slicer needs in order to fill it.
    /// </summary>
    /// <param name="fixtureName">The fixture.</param>
    [Theory]
    [InlineData("star")]
    [InlineData("lshape")]
    [InlineData("narrowwaist")]
    [InlineData("cat")]
    public void SolidIsWatertight(string fixtureName)
    {
        (float X, float Y, float Z)[][] triangles = ReadTriangles(StlWriter.Build(TestFixtures.Build(fixtureName), 200, 8));

        // Each undirected edge must appear twice, and each directed edge once, which
        // together mean the surface is closed and consistently wound.
        var directed = new HashSet<((float, float, float), (float, float, float))>();
        var undirected = new Dictionary<((float, float, float), (float, float, float)), int>();

        foreach ((float X, float Y, float Z)[] triangle in triangles)
        {
            for (int i = 0; i < 3; i++)
            {
                (float, float, float) a = triangle[i];
                (float, float, float) b = triangle[(i + 1) % 3];

                Assert.True(directed.Add((a, b)), $"The directed edge {a} to {b} appears twice.");

                var key = Compare(a, b) <= 0 ? (a, b) : (b, a);
                undirected[key] = undirected.GetValueOrDefault(key) + 1;
            }
        }

        foreach ((((float, float, float), (float, float, float)) edge, int uses) in undirected)
        {
            Assert.True(uses == 2, $"The edge {edge} is used by {uses} triangles, not 2.");
        }
    }

    /// <summary>Every normal points away from the centre of the solid.</summary>
    [Fact]
    public void NormalsPointOutwards()
    {
        byte[] stl = StlWriter.Build(TestFixtures.Cat(), 200, 12);
        (float X, float Y, float Z)[][] triangles = ReadTriangles(stl);
        (float X, float Y, float Z)[] normals = ReadNormals(stl);

        // The centroid of a convex-ish solid is inside it; the cat fixture is star shaped
        // about its centre, so the test holds for every face.
        double cx = 0;
        double cy = 0;
        double cz = 0;
        int points = 0;
        foreach ((float X, float Y, float Z)[] triangle in triangles)
        {
            foreach ((float X, float Y, float Z) vertex in triangle)
            {
                cx += vertex.X;
                cy += vertex.Y;
                cz += vertex.Z;
                points++;
            }
        }

        cx /= points;
        cy /= points;
        cz /= points;

        for (int i = 0; i < triangles.Length; i++)
        {
            (float X, float Y, float Z)[] t = triangles[i];
            double mx = ((double)t[0].X + t[1].X + t[2].X) / 3;
            double my = ((double)t[0].Y + t[1].Y + t[2].Y) / 3;
            double mz = ((double)t[0].Z + t[1].Z + t[2].Z) / 3;

            double outward = ((mx - cx) * normals[i].X)
                + ((my - cy) * normals[i].Y)
                + ((mz - cz) * normals[i].Z);

            Assert.True(outward > -1e-3, $"Triangle {i} has a normal pointing inwards.");
        }
    }

    /// <summary>The solid has the requested footprint and thickness, in millimetres.</summary>
    [Fact]
    public void DimensionsAreAsRequested()
    {
        const double Size = 180;
        const double Height = 7.5;

        (float X, float Y, float Z)[][] triangles = ReadTriangles(StlWriter.Build(TestFixtures.Star(), Size, Height));

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach ((float X, float Y, float Z)[] triangle in triangles)
        {
            foreach ((float X, float Y, float Z) vertex in triangle)
            {
                minX = Math.Min(minX, vertex.X);
                maxX = Math.Max(maxX, vertex.X);
                minY = Math.Min(minY, vertex.Y);
                maxY = Math.Max(maxY, vertex.Y);
                minZ = Math.Min(minZ, vertex.Z);
                maxZ = Math.Max(maxZ, vertex.Z);
            }
        }

        Assert.Equal(Size, Math.Max(maxX - minX, maxY - minY), 1e-3);
        Assert.Equal(0.0, minZ, 1e-6);
        Assert.Equal(Height, maxZ, 1e-4);

        // Centred on the origin in the plane.
        Assert.Equal(0.0, (minX + maxX) / 2, 1e-3);
        Assert.Equal(0.0, (minY + maxY) / 2, 1e-3);
    }

    /// <summary>The triangulation covers the outline exactly, so the areas agree.</summary>
    /// <param name="fixtureName">The fixture.</param>
    [Theory]
    [InlineData("star")]
    [InlineData("lshape")]
    [InlineData("narrowwaist")]
    [InlineData("cat")]
    public void TriangulationCoversTheOutline(string fixtureName)
    {
        Vec2[] shape = TestFixtures.Build(fixtureName);
        int[] triangles = StlWriter.Triangulate(shape);

        Assert.Equal(3 * (shape.Length - 2), triangles.Length);

        double total = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vec2 a = shape[triangles[i]];
            Vec2 b = shape[triangles[i + 1]];
            Vec2 c = shape[triangles[i + 2]];
            double area = (b - a).Cross(c - a) / 2;

            Assert.True(area > 0, "An ear was clipped with the wrong orientation.");
            total += area;
        }

        Assert.Equal(PolygonOps.SignedArea(shape), total, 1e-9);
    }

    /// <summary>A height of zero or less is rejected.</summary>
    [Fact]
    public void NonPositiveHeightIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StlWriter.Build(TestFixtures.Star(), 200, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StlWriter.Build(TestFixtures.Star(), 200, -1));
    }

    /// <summary>Reads the vertices of every triangle in a binary STL.</summary>
    /// <param name="stl">The file content.</param>
    /// <returns>Three vertices per triangle.</returns>
    private static (float X, float Y, float Z)[][] ReadTriangles(byte[] stl)
    {
        uint count = BitConverter.ToUInt32(stl, HeaderBytes);
        var result = new (float, float, float)[count][];

        for (int t = 0; t < count; t++)
        {
            int offset = HeaderBytes + 4 + (t * TriangleBytes) + 12;
            result[t] =
            [
                ReadVector(stl, offset),
                ReadVector(stl, offset + 12),
                ReadVector(stl, offset + 24),
            ];
        }

        return result;
    }

    /// <summary>Reads the normal of every triangle.</summary>
    /// <param name="stl">The file content.</param>
    /// <returns>One normal per triangle.</returns>
    private static (float X, float Y, float Z)[] ReadNormals(byte[] stl)
    {
        uint count = BitConverter.ToUInt32(stl, HeaderBytes);
        var result = new (float, float, float)[count];
        for (int t = 0; t < count; t++)
        {
            result[t] = ReadVector(stl, HeaderBytes + 4 + (t * TriangleBytes));
        }

        return result;
    }

    /// <summary>Reads one vector of three single precision values.</summary>
    /// <param name="stl">The file content.</param>
    /// <param name="offset">Where the vector starts.</param>
    /// <returns>The vector.</returns>
    private static (float X, float Y, float Z) ReadVector(byte[] stl, int offset) =>
        (BitConverter.ToSingle(stl, offset),
         BitConverter.ToSingle(stl, offset + 4),
         BitConverter.ToSingle(stl, offset + 8));

    /// <summary>Orders two vertices, so that an undirected edge has one key.</summary>
    /// <param name="a">The first vertex.</param>
    /// <param name="b">The second vertex.</param>
    /// <returns>A negative value when the first sorts first.</returns>
    private static int Compare((float X, float Y, float Z) a, (float X, float Y, float Z) b)
    {
        int byX = a.X.CompareTo(b.X);
        if (byX != 0)
        {
            return byX;
        }

        int byY = a.Y.CompareTo(b.Y);
        return byY != 0 ? byY : a.Z.CompareTo(b.Z);
    }
}
