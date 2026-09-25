namespace Escherize.Parametrization;

/// <summary>
/// Computes an orthonormal basis of the null space of a small dense matrix by Gaussian
/// elimination with full pivoting, followed by two passes of modified Gram-Schmidt
/// (SPEC §5.3).
/// </summary>
public static class NullspaceSolver
{
    /// <summary>The pivot tolerance of the elimination (SPEC §5.3).</summary>
    public const double PivotTolerance = 1e-12;

    /// <summary>
    /// The orthonormal null space basis of <paramref name="matrix"/>, with one column per
    /// degree of freedom.
    /// </summary>
    /// <param name="matrix">The constraint matrix.</param>
    /// <returns>A matrix whose columns span the null space.</returns>
    public static DenseMatrix Nullspace(DenseMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        int rows = matrix.Rows;
        int columns = matrix.Columns;

        // Work on a copy; the column permutation is tracked so the result can be
        // expressed in the original variable order.
        var work = new DenseMatrix(rows, columns);
        for (int r = 0; r < rows; r++)
        {
            matrix.Row(r).CopyTo(work.Row(r));
        }

        var columnOrder = new int[columns];
        for (int c = 0; c < columns; c++)
        {
            columnOrder[c] = c;
        }

        int rank = 0;
        for (int step = 0; step < Math.Min(rows, columns); step++)
        {
            // Full pivoting: search the whole remaining block for the largest entry.
            double best = 0;
            int bestRow = -1;
            int bestColumn = -1;
            for (int r = step; r < rows; r++)
            {
                for (int c = step; c < columns; c++)
                {
                    double magnitude = Math.Abs(work[r, c]);
                    if (magnitude > best)
                    {
                        best = magnitude;
                        bestRow = r;
                        bestColumn = c;
                    }
                }
            }

            if (bestRow < 0 || best <= PivotTolerance)
            {
                break;
            }

            SwapRows(work, step, bestRow);
            SwapColumns(work, step, bestColumn);
            (columnOrder[step], columnOrder[bestColumn]) = (columnOrder[bestColumn], columnOrder[step]);

            // Normalise the pivot row and clear the pivot column everywhere else, which
            // leaves the matrix in reduced row echelon form.
            double pivot = work[step, step];
            Span<double> pivotRow = work.Row(step);
            for (int c = 0; c < columns; c++)
            {
                pivotRow[c] /= pivot;
            }

            for (int r = 0; r < rows; r++)
            {
                if (r == step)
                {
                    continue;
                }

                double factor = work[r, step];
                if (factor != 0)
                {
                    work.AddToRow(r, pivotRow, -factor);
                }
            }

            rank++;
        }

        int nullity = columns - rank;
        var basis = new DenseMatrix(columns, nullity);

        // Each free variable gives one basis vector: set it to one, the other free
        // variables to zero, and read the pivot variables off the echelon form.
        for (int f = 0; f < nullity; f++)
        {
            int freeColumn = rank + f;
            basis[columnOrder[freeColumn], f] = 1.0;
            for (int p = 0; p < rank; p++)
            {
                basis[columnOrder[p], f] = -work[p, freeColumn];
            }
        }

        Orthonormalize(basis);
        return basis;
    }

    /// <summary>
    /// Orthonormalises the columns in place with two passes of modified Gram-Schmidt,
    /// which keeps the result orthonormal to close to machine precision (SPEC §5.3).
    /// </summary>
    /// <param name="matrix">The matrix whose columns are orthonormalised.</param>
    /// <exception cref="InvalidOperationException">The columns are linearly dependent.</exception>
    public static void Orthonormalize(DenseMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        for (int c = 0; c < matrix.Columns; c++)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int previous = 0; previous < c; previous++)
                {
                    double projection = 0;
                    for (int r = 0; r < matrix.Rows; r++)
                    {
                        projection += matrix[r, previous] * matrix[r, c];
                    }

                    for (int r = 0; r < matrix.Rows; r++)
                    {
                        matrix[r, c] -= projection * matrix[r, previous];
                    }
                }
            }

            double norm = 0;
            for (int r = 0; r < matrix.Rows; r++)
            {
                norm += matrix[r, c] * matrix[r, c];
            }

            norm = Math.Sqrt(norm);
            if (norm <= PivotTolerance)
            {
                throw new InvalidOperationException("The basis columns are linearly dependent.");
            }

            for (int r = 0; r < matrix.Rows; r++)
            {
                matrix[r, c] /= norm;
            }
        }
    }

    /// <summary>Swaps two rows in place.</summary>
    /// <param name="matrix">The matrix.</param>
    /// <param name="first">The first row.</param>
    /// <param name="second">The second row.</param>
    private static void SwapRows(DenseMatrix matrix, int first, int second)
    {
        if (first == second)
        {
            return;
        }

        for (int c = 0; c < matrix.Columns; c++)
        {
            (matrix[first, c], matrix[second, c]) = (matrix[second, c], matrix[first, c]);
        }
    }

    /// <summary>Swaps two columns in place.</summary>
    /// <param name="matrix">The matrix.</param>
    /// <param name="first">The first column.</param>
    /// <param name="second">The second column.</param>
    private static void SwapColumns(DenseMatrix matrix, int first, int second)
    {
        if (first == second)
        {
            return;
        }

        for (int r = 0; r < matrix.Rows; r++)
        {
            (matrix[r, first], matrix[r, second]) = (matrix[r, second], matrix[r, first]);
        }
    }
}
