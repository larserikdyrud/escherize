using Escherize.Geometry;
using StbImageSharp;

namespace Escherize.Imaging;

/// <summary>Options for reading a silhouette from a raster image (SPEC §4.1).</summary>
public sealed record ImageContourOptions
{
    /// <summary>
    /// The luminance threshold, the <c>--threshold</c> flag. When null the threshold is
    /// chosen automatically with the Otsu method.
    /// </summary>
    public int? Threshold { get; init; }

    /// <summary>
    /// Whether the foreground is light instead of dark, the <c>--invert</c> flag. The
    /// default is a dark foreground on a light background.
    /// </summary>
    public bool Invert { get; init; }
}

/// <summary>
/// Decodes a raster image into a binary silhouette and extracts its outer contour
/// (SPEC §4.1).
/// </summary>
public static class PngContourExtractor
{
    /// <summary>The result of reading a silhouette.</summary>
    /// <param name="Contour">The outer contour, y-up and positively oriented.</param>
    /// <param name="Threshold">The luminance threshold that was applied.</param>
    /// <param name="Mask">The binary mask the contour was traced from.</param>
    public sealed record Result(Vec2[] Contour, int Threshold, BinaryMask Mask);

    /// <summary>Reads a silhouette contour from an image file.</summary>
    /// <param name="path">The image file.</param>
    /// <param name="options">The threshold options.</param>
    /// <returns>The contour and the mask it came from.</returns>
    /// <exception cref="InvalidDataException">The image could not be decoded or holds no silhouette.</exception>
    public static Result Read(string path, ImageContourOptions? options = null)
    {
        using FileStream stream = File.OpenRead(path);
        return Read(stream, options);
    }

    /// <summary>Reads a silhouette contour from an image stream.</summary>
    /// <param name="stream">The image data.</param>
    /// <param name="options">The threshold options.</param>
    /// <returns>The contour and the mask it came from.</returns>
    /// <exception cref="InvalidDataException">The image could not be decoded or holds no silhouette.</exception>
    public static Result Read(Stream stream, ImageContourOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new ImageContourOptions();

        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha)
            ?? throw new InvalidDataException("The image could not be decoded.");

        return FromRgba(image.Data, image.Width, image.Height, options);
    }

    /// <summary>Builds a silhouette contour from raw RGBA pixels.</summary>
    /// <param name="rgba">The pixel data, four bytes per pixel, row major.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="options">The threshold options.</param>
    /// <returns>The contour and the mask it came from.</returns>
    /// <exception cref="InvalidDataException">The image holds no silhouette.</exception>
    public static Result FromRgba(ReadOnlySpan<byte> rgba, int width, int height, ImageContourOptions? options = null)
    {
        options ??= new ImageContourOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException("The pixel buffer is smaller than the image.", nameof(rgba));
        }

        // Luminance, with fully transparent pixels marked as background (SPEC §4.1).
        var luminance = new byte[width * height];
        var opaque = new bool[width * height];
        for (int i = 0; i < luminance.Length; i++)
        {
            int offset = i * 4;
            byte alpha = rgba[offset + 3];
            opaque[i] = alpha >= ImagingInfo.AlphaBackgroundThreshold;
            double value = (0.299 * rgba[offset]) + (0.587 * rgba[offset + 1]) + (0.114 * rgba[offset + 2]);
            luminance[i] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        }

        int threshold = options.Threshold ?? OtsuThreshold(luminance, opaque);

        var mask = new BinaryMask(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                if (!opaque[index])
                {
                    continue;
                }

                bool dark = luminance[index] <= threshold;
                mask[x, y] = options.Invert ? !dark : dark;
            }
        }

        BinaryMask cleaned = mask.KeepLargestComponentAndFillHoles();
        Vec2[] traced = MarchingSquares.TraceLongestContour(cleaned);

        // Image rows run downwards, so the contour is mirrored and re-oriented (SPEC §3).
        Vec2[] flipped = PolygonOps.FlipY(traced);
        Vec2[] oriented = PolygonOps.EnsurePositiveOrientation(flipped);
        return new Result(oriented, threshold, cleaned);
    }

    /// <summary>
    /// The Otsu threshold of the luminance histogram, computed over the opaque pixels
    /// only (SPEC §4.1).
    /// </summary>
    /// <param name="luminance">The luminance of every pixel.</param>
    /// <param name="opaque">Which pixels count towards the histogram.</param>
    /// <returns>The threshold in the range 0 to 255.</returns>
    /// <exception cref="InvalidDataException">Every pixel is transparent.</exception>
    public static int OtsuThreshold(ReadOnlySpan<byte> luminance, ReadOnlySpan<bool> opaque)
    {
        Span<long> histogram = stackalloc long[256];
        long total = 0;
        for (int i = 0; i < luminance.Length; i++)
        {
            if (opaque[i])
            {
                histogram[luminance[i]]++;
                total++;
            }
        }

        if (total == 0)
        {
            throw new InvalidDataException("The image is fully transparent.");
        }

        double sum = 0;
        for (int v = 0; v < 256; v++)
        {
            sum += (double)v * histogram[v];
        }

        double sumBackground = 0;
        long weightBackground = 0;
        double bestVariance = -1;
        int bestThreshold = 0;

        for (int v = 0; v < 256; v++)
        {
            weightBackground += histogram[v];
            if (weightBackground == 0)
            {
                continue;
            }

            long weightForeground = total - weightBackground;
            if (weightForeground == 0)
            {
                break;
            }

            sumBackground += (double)v * histogram[v];
            double meanBackground = sumBackground / weightBackground;
            double meanForeground = (sum - sumBackground) / weightForeground;
            double difference = meanBackground - meanForeground;
            double variance = (double)weightBackground * weightForeground * difference * difference;

            if (variance > bestVariance)
            {
                bestVariance = variance;
                bestThreshold = v;
            }
        }

        return bestThreshold;
    }
}
