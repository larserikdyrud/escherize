using System.IO.Compression;
using Escherize.Geometry;

namespace Escherize.Tests;

/// <summary>Shape and image generators shared by the tests.</summary>
internal static class TestShapes
{
    /// <summary>A circle sampled at equal angles, counter-clockwise.</summary>
    /// <param name="count">The number of points.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="center">The centre.</param>
    /// <returns>The circle.</returns>
    public static Vec2[] Circle(int count, double radius = 1.0, Vec2 center = default)
    {
        var points = new Vec2[count];
        for (int i = 0; i < count; i++)
        {
            double angle = 2 * Math.PI * i / count;
            points[i] = new Vec2(center.X + (radius * Math.Cos(angle)), center.Y + (radius * Math.Sin(angle)));
        }

        return points;
    }

    /// <summary>An axis aligned square with a vertex at the origin, counter-clockwise.</summary>
    /// <param name="side">The side length.</param>
    /// <returns>The four corners.</returns>
    public static Vec2[] Square(double side = 1.0) =>
    [
        new Vec2(0, 0),
        new Vec2(side, 0),
        new Vec2(side, side),
        new Vec2(0, side),
    ];

    /// <summary>An L shaped polygon, counter-clockwise.</summary>
    /// <returns>The six corners.</returns>
    public static Vec2[] LShape() =>
    [
        new Vec2(0, 0),
        new Vec2(3, 0),
        new Vec2(3, 1),
        new Vec2(1, 1),
        new Vec2(1, 3),
        new Vec2(0, 3),
    ];

    /// <summary>
    /// Renders a filled dark disc on a white background and encodes it as a PNG, so that
    /// the image pipeline can be tested without a binary fixture (SPEC §8.1).
    /// </summary>
    /// <param name="radius">The disc radius in pixels.</param>
    /// <param name="margin">The margin around the disc in pixels.</param>
    /// <returns>The PNG file content.</returns>
    public static byte[] CirclePng(int radius, int margin = 20)
    {
        int size = (2 * (radius + margin)) + 1;
        double center = (size - 1) / 2.0;

        var rgba = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - center;
                double dy = y - center;
                bool inside = (dx * dx) + (dy * dy) <= (double)radius * radius;
                byte value = inside ? (byte)0 : (byte)255;

                int offset = ((y * size) + x) * 4;
                rgba[offset] = value;
                rgba[offset + 1] = value;
                rgba[offset + 2] = value;
                rgba[offset + 3] = 255;
            }
        }

        return EncodePng(rgba, size, size);
    }

    /// <summary>
    /// Encodes RGBA pixels as a PNG using a zlib stream with no filtering. Only the tests
    /// need to write images, so a minimal encoder is enough.
    /// </summary>
    /// <param name="rgba">The pixels, four bytes each, row major.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The PNG file content.</returns>
    public static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        // Raw scan lines, each preceded by filter type 0.
        var raw = new byte[height * ((width * 4) + 1)];
        int position = 0;
        for (int y = 0; y < height; y++)
        {
            raw[position++] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, position, width * 4);
            position += width * 4;
        }

        byte[] compressed = ZlibCompress(raw);

        using var stream = new MemoryStream();
        stream.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // colour type: truecolour with alpha
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // no interlace

        WriteChunk(stream, "IHDR", header);
        WriteChunk(stream, "IDAT", compressed);
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    /// <summary>Wraps deflate output in a zlib container with an Adler-32 checksum.</summary>
    /// <param name="data">The data to compress.</param>
    /// <returns>The zlib stream.</returns>
    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78); // deflate, 32k window
        output.WriteByte(0x01); // no dictionary, fastest

        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        uint a = 1;
        uint b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        var adler = new byte[4];
        WriteBigEndian(adler, 0, (b << 16) | a);
        output.Write(adler);
        return output.ToArray();
    }

    /// <summary>Writes one PNG chunk with its length, type, payload and CRC.</summary>
    /// <param name="stream">The output stream.</param>
    /// <param name="type">The four character chunk type.</param>
    /// <param name="payload">The chunk payload.</param>
    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)payload.Length);
        stream.Write(length);

        var typeAndPayload = new byte[4 + payload.Length];
        for (int i = 0; i < 4; i++)
        {
            typeAndPayload[i] = (byte)type[i];
        }

        Buffer.BlockCopy(payload, 0, typeAndPayload, 4, payload.Length);
        stream.Write(typeAndPayload);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32(typeAndPayload));
        stream.Write(crc);
    }

    /// <summary>Writes a 32 bit value in network byte order.</summary>
    /// <param name="buffer">The destination.</param>
    /// <param name="offset">The offset to write at.</param>
    /// <param name="value">The value.</param>
    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>The CRC-32 used by PNG chunks.</summary>
    /// <param name="data">The data.</param>
    /// <returns>The checksum.</returns>
    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFF;
    }
}
