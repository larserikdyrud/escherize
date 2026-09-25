namespace Escherize.Parametrization;

/// <summary>
/// A small dense row major matrix. Only the operations the parametrisation needs are
/// provided; SPEC §2 keeps Escherize.Core on the base class library, so there is no
/// external linear algebra package.
/// </summary>
public sealed class DenseMatrix
{
    private readonly double[] _values;

    /// <summary>Creates a zero matrix.</summary>
    /// <param name="rows">The number of rows.</param>
    /// <param name="columns">The number of columns.</param>
    public DenseMatrix(int rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(columns);

        Rows = rows;
        Columns = columns;
        _values = new double[rows * columns];
    }

    /// <summary>The number of rows.</summary>
    public int Rows { get; }

    /// <summary>The number of columns.</summary>
    public int Columns { get; }

    /// <summary>The backing storage, row major.</summary>
    public ReadOnlySpan<double> Values => _values;

    /// <summary>Gets or sets one entry.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns>The entry.</returns>
    public double this[int row, int column]
    {
        get => _values[(row * Columns) + column];
        set => _values[(row * Columns) + column] = value;
    }

    /// <summary>The storage of one row.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The row as a span.</returns>
    public Span<double> Row(int row) => _values.AsSpan(row * Columns, Columns);

    /// <summary>Adds a multiple of a span to one row.</summary>
    /// <param name="row">The row.</param>
    /// <param name="source">The values to add.</param>
    /// <param name="factor">The factor applied to <paramref name="source"/>.</param>
    public void AddToRow(int row, ReadOnlySpan<double> source, double factor)
    {
        Span<double> target = Row(row);
        for (int i = 0; i < target.Length; i++)
        {
            target[i] += factor * source[i];
        }
    }

    /// <summary>Computes the Gram matrix, this transposed times this.</summary>
    /// <returns>A square matrix of size <see cref="Columns"/>.</returns>
    public DenseMatrix GramMatrix()
    {
        var gram = new DenseMatrix(Columns, Columns);
        for (int r = 0; r < Rows; r++)
        {
            ReadOnlySpan<double> row = Row(r);
            for (int i = 0; i < Columns; i++)
            {
                double value = row[i];
                if (value == 0)
                {
                    continue;
                }

                for (int j = 0; j < Columns; j++)
                {
                    gram[i, j] += value * row[j];
                }
            }
        }

        return gram;
    }

    /// <summary>
    /// The Cholesky factor L of a symmetric positive definite matrix, so that the matrix
    /// equals L times L transposed (SPEC §5.5).
    /// </summary>
    /// <returns>The lower triangular factor.</returns>
    /// <exception cref="InvalidOperationException">The matrix is not positive definite.</exception>
    public DenseMatrix Cholesky()
    {
        if (Rows != Columns)
        {
            throw new InvalidOperationException("Only a square matrix can be factorised.");
        }

        var l = new DenseMatrix(Rows, Rows);
        for (int i = 0; i < Rows; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = this[i, j];
                for (int p = 0; p < j; p++)
                {
                    sum -= l[i, p] * l[j, p];
                }

                if (i == j)
                {
                    if (sum <= 0)
                    {
                        throw new InvalidOperationException(
                            "The matrix is not positive definite, so the parametrisation is degenerate.");
                    }

                    l[i, j] = Math.Sqrt(sum);
                }
                else
                {
                    l[i, j] = sum / l[j, j];
                }
            }
        }

        return l;
    }

    /// <summary>
    /// Right multiplies every row by the inverse transpose of a lower triangular factor,
    /// producing this times L inverse transposed (SPEC §5.5 step 4).
    /// </summary>
    /// <param name="lower">The lower triangular factor L.</param>
    /// <returns>The transformed matrix.</returns>
    public DenseMatrix MultiplyByInverseTransposeOf(DenseMatrix lower)
    {
        ArgumentNullException.ThrowIfNull(lower);

        // Row b of the result solves L y = b by forward substitution, because
        // b L^-T has the transpose (L^-1 b^T).
        var result = new DenseMatrix(Rows, Columns);
        for (int r = 0; r < Rows; r++)
        {
            ReadOnlySpan<double> source = Row(r);
            Span<double> target = result.Row(r);
            for (int i = 0; i < Columns; i++)
            {
                double sum = source[i];
                for (int p = 0; p < i; p++)
                {
                    sum -= lower[i, p] * target[p];
                }

                target[i] = sum / lower[i, i];
            }
        }

        return result;
    }

    /// <summary>Multiplies the matrix by a column vector.</summary>
    /// <param name="vector">The vector, of length <see cref="Columns"/>.</param>
    /// <returns>The product, of length <see cref="Rows"/>.</returns>
    public double[] Multiply(ReadOnlySpan<double> vector)
    {
        if (vector.Length != Columns)
        {
            throw new ArgumentException($"Expected {Columns} entries, got {vector.Length}.", nameof(vector));
        }

        var result = new double[Rows];
        for (int r = 0; r < Rows; r++)
        {
            ReadOnlySpan<double> row = Row(r);
            double sum = 0;
            for (int c = 0; c < Columns; c++)
            {
                sum += row[c] * vector[c];
            }

            result[r] = sum;
        }

        return result;
    }

    /// <summary>Multiplies the transpose of the matrix by a column vector.</summary>
    /// <param name="vector">The vector, of length <see cref="Rows"/>.</param>
    /// <returns>The product, of length <see cref="Columns"/>.</returns>
    public double[] TransposeMultiply(ReadOnlySpan<double> vector)
    {
        if (vector.Length != Rows)
        {
            throw new ArgumentException($"Expected {Rows} entries, got {vector.Length}.", nameof(vector));
        }

        var result = new double[Columns];
        for (int r = 0; r < Rows; r++)
        {
            double value = vector[r];
            if (value == 0)
            {
                continue;
            }

            ReadOnlySpan<double> row = Row(r);
            for (int c = 0; c < Columns; c++)
            {
                result[c] += value * row[c];
            }
        }

        return result;
    }
}
