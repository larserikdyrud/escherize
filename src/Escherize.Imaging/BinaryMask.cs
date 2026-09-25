namespace Escherize.Imaging;

/// <summary>
/// A binary foreground mask on a pixel grid. Row major, one byte per pixel, one for
/// foreground and zero for background.
/// </summary>
public sealed class BinaryMask
{
    private readonly byte[] _pixels;

    /// <summary>Creates an all background mask.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public BinaryMask(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        _pixels = new byte[width * height];
    }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>The number of foreground pixels.</summary>
    public int ForegroundCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _pixels.Length; i++)
            {
                count += _pixels[i];
            }

            return count;
        }
    }

    /// <summary>Gets or sets a pixel. Coordinates outside the grid read as background.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>True when the pixel is foreground.</returns>
    public bool this[int x, int y]
    {
        get => (uint)x < (uint)Width && (uint)y < (uint)Height && _pixels[(y * Width) + x] != 0;
        set
        {
            if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            {
                _pixels[(y * Width) + x] = value ? (byte)1 : (byte)0;
            }
        }
    }

    /// <summary>
    /// Keeps only the largest eight-connected foreground component and then fills the
    /// holes inside it, by flood filling the background from the image border and
    /// treating everything the fill does not reach as foreground (SPEC §4.1).
    /// </summary>
    /// <returns>The cleaned mask.</returns>
    /// <exception cref="InvalidDataException">The mask has no foreground pixel.</exception>
    public BinaryMask KeepLargestComponentAndFillHoles()
    {
        BinaryMask largest = KeepLargestComponent();
        largest.FillHoles();
        return largest;
    }

    /// <summary>Keeps only the largest eight-connected foreground component (SPEC §4.1).</summary>
    /// <returns>A mask holding that component alone.</returns>
    /// <exception cref="InvalidDataException">The mask has no foreground pixel.</exception>
    public BinaryMask KeepLargestComponent()
    {
        var labels = new int[Width * Height];
        var stack = new Stack<int>();
        int bestLabel = 0;
        int bestSize = 0;
        int label = 0;

        for (int start = 0; start < _pixels.Length; start++)
        {
            if (_pixels[start] == 0 || labels[start] != 0)
            {
                continue;
            }

            label++;
            int size = 0;
            stack.Push(start);
            labels[start] = label;

            while (stack.Count > 0)
            {
                int index = stack.Pop();
                size++;
                int x = index % Width;
                int y = index / Width;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        int nx = x + dx;
                        int ny = y + dy;
                        if ((uint)nx >= (uint)Width || (uint)ny >= (uint)Height)
                        {
                            continue;
                        }

                        int neighbour = (ny * Width) + nx;
                        if (_pixels[neighbour] != 0 && labels[neighbour] == 0)
                        {
                            labels[neighbour] = label;
                            stack.Push(neighbour);
                        }
                    }
                }
            }

            if (size > bestSize)
            {
                bestSize = size;
                bestLabel = label;
            }
        }

        if (bestSize == 0)
        {
            throw new InvalidDataException("The image has no foreground pixel after thresholding.");
        }

        var result = new BinaryMask(Width, Height);
        for (int i = 0; i < labels.Length; i++)
        {
            result._pixels[i] = labels[i] == bestLabel ? (byte)1 : (byte)0;
        }

        return result;
    }

    /// <summary>
    /// Fills enclosed background regions. The background is flood filled with four
    /// connectivity from the image border, which is the complement of eight connected
    /// foreground; everything the fill misses becomes foreground (SPEC §4.1).
    /// </summary>
    public void FillHoles()
    {
        var reached = new bool[Width * Height];
        var stack = new Stack<int>();

        void Seed(int x, int y)
        {
            int index = (y * Width) + x;
            if (_pixels[index] == 0 && !reached[index])
            {
                reached[index] = true;
                stack.Push(index);
            }
        }

        for (int x = 0; x < Width; x++)
        {
            Seed(x, 0);
            Seed(x, Height - 1);
        }

        for (int y = 0; y < Height; y++)
        {
            Seed(0, y);
            Seed(Width - 1, y);
        }

        while (stack.Count > 0)
        {
            int index = stack.Pop();
            int x = index % Width;
            int y = index / Width;

            if (x > 0)
            {
                Seed(x - 1, y);
            }

            if (x + 1 < Width)
            {
                Seed(x + 1, y);
            }

            if (y > 0)
            {
                Seed(x, y - 1);
            }

            if (y + 1 < Height)
            {
                Seed(x, y + 1);
            }
        }

        for (int i = 0; i < _pixels.Length; i++)
        {
            if (_pixels[i] == 0 && !reached[i])
            {
                _pixels[i] = 1;
            }
        }
    }
}
