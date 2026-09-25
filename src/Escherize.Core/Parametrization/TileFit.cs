namespace Escherize.Parametrization;

/// <summary>The best tile in a template family for a given goal (SPEC §6.1, §6.4).</summary>
/// <param name="Xi">The parameter vector of the fitted tile.</param>
/// <param name="Error">The distance measure e, which is at least zero.</param>
public readonly record struct TileFitResult(double[] Xi, double Error);

/// <summary>
/// Fits a goal to a template family with the two distance measures of SPEC §6.1: the
/// plain euclidean projection, and the Procrustes measure that leaves the rotation free
/// and is the right one for every template containing a glide pair.
/// </summary>
public static class TileFit
{
    /// <summary>
    /// The euclidean fit: e = ||w||^2 - ||B^T w||^2, with the optimal parameters
    /// xi = B^T w (SPEC §6.1, §6.4).
    /// </summary>
    /// <param name="basis">The tile basis.</param>
    /// <param name="w">The goal coordinate vector of length 2n.</param>
    /// <returns>The fit.</returns>
    public static TileFitResult Euclid(TileBasis basis, ReadOnlySpan<double> w)
    {
        ArgumentNullException.ThrowIfNull(basis);

        double[] p = basis.Project(w);
        double normSquared = 0;
        for (int i = 0; i < w.Length; i++)
        {
            normSquared += w[i] * w[i];
        }

        double projected = 0;
        foreach (double value in p)
        {
            projected += value * value;
        }

        return new TileFitResult(p, Math.Max(0, normSquared - projected));
    }

    /// <summary>
    /// The Procrustes fit, where the rotation is free and the scale is fixed
    /// (SPEC §6.1, §6.4).
    /// </summary>
    /// <param name="basis">The tile basis.</param>
    /// <param name="w">The goal coordinate vector of length 2n.</param>
    /// <returns>The fit.</returns>
    public static TileFitResult Procrustes(TileBasis basis, ReadOnlySpan<double> w)
    {
        ArgumentNullException.ThrowIfNull(basis);

        int n = w.Length / 2;

        // w_c is w rotated by minus ninety degrees: (x, y) becomes (y, -x).
        var wc = new double[w.Length];
        for (int t = 0; t < n; t++)
        {
            wc[t] = w[n + t];
            wc[n + t] = -w[t];
        }

        double[] p = basis.Project(w);
        double[] pc = basis.Project(wc);

        double g11 = 0;
        double g22 = 0;
        double g12 = 0;
        for (int i = 0; i < p.Length; i++)
        {
            g11 += p[i] * p[i];
            g22 += pc[i] * pc[i];
            g12 += p[i] * pc[i];
        }

        double half = (g11 - g22) / 2;
        double lambda = ((g11 + g22) / 2) + Math.Sqrt((half * half) + (g12 * g12));

        // The unit eigenvector of the two by two matrix for the larger eigenvalue.
        double vx = lambda - g22;
        double vy = g12;
        double length = Math.Sqrt((vx * vx) + (vy * vy));
        if (length <= 1e-300)
        {
            vx = 1;
            vy = 0;
            length = 1;
        }

        vx /= length;
        vy /= length;

        var xi = new double[p.Length];
        for (int i = 0; i < xi.Length; i++)
        {
            xi[i] = (p[i] * vx) + (pc[i] * vy);
        }

        double normSquared = 0;
        for (int i = 0; i < w.Length; i++)
        {
            normSquared += w[i] * w[i];
        }

        return new TileFitResult(xi, Math.Max(0, normSquared - lambda));
    }

    /// <summary>Fits with the measure the template calls for (SPEC §5.2, §6.1).</summary>
    /// <param name="basis">The tile basis.</param>
    /// <param name="w">The goal coordinate vector of length 2n.</param>
    /// <returns>The fit.</returns>
    public static TileFitResult Fit(TileBasis basis, ReadOnlySpan<double> w)
    {
        ArgumentNullException.ThrowIfNull(basis);
        return basis.Template.UsesProcrustes ? Procrustes(basis, w) : Euclid(basis, w);
    }
}
