using Escherize.Parametrization;
using Escherize.Search;
using Escherize.Templates;
using MathNet.Numerics.LinearAlgebra;
using Xunit;

namespace Escherize.Tests;

/// <summary>
/// Oracle tests (SPEC §8.4). The fast evaluator, the dense evaluator and an independent
/// implementation built on MathNet must agree to 1e-9.
/// </summary>
public sealed class OracleTests
{
    /// <summary>The tolerance the three implementations must agree within (SPEC §8.4).</summary>
    private const double Tolerance = 1e-9;

    /// <summary>The number of random cases per template (SPEC §8.4).</summary>
    private const int CasesPerTemplate = 50;

    /// <summary>The number of goal points the cases use.</summary>
    private const int PointCount = 24;

    /// <summary>Every template name, as test data.</summary>
    /// <returns>The names.</returns>
    public static TheoryData<string> TemplateNames()
    {
        var data = new TheoryData<string>();
        foreach (string name in TemplateLibrary.Names)
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>
    /// For fifty random combinations of goal, k vector, offset and orientation, the fast
    /// evaluator agrees with the dense one and both agree with the oracle (SPEC §8.4).
    /// </summary>
    /// <param name="templateName">The template.</param>
    [Theory]
    [MemberData(nameof(TemplateNames))]
    public void FastDenseAndOracleAgree(string templateName)
    {
        TemplateSpec template = TemplateLibrary.ByName(templateName);
        int[][] kVectors = [.. KVectorEnumerator.Enumerate(template, PointCount, minimumK: 0)];
        Assert.NotEmpty(kVectors);

        var random = new Random(20260925);

        for (int c = 0; c < CasesPerTemplate; c++)
        {
            double[] w = TemplateValidator.NoisyCircle(PointCount, random.Next());
            int[] k = kVectors[random.Next(kVectors.Length)];
            int j = random.Next(PointCount);

            var tables = new GoalTables(w);
            TileLayout layout = TileLayout.Create(template, k);
            BasisPlan plan = BasisPlan.Create(layout);

            double fast = new FastEvaluator(plan, tables).Evaluate(j);
            double dense = DenseError(DenseBasisBuilder.Build(plan), w, j);
            double oracle = OracleError(layout, w, j);

            Assert.True(
                Math.Abs(fast - dense) <= Tolerance,
                $"{templateName} k=[{string.Join(",", k)}] j={j}: fast {fast:0.###############} " +
                $"vs dense {dense:0.###############}, difference {Math.Abs(fast - dense):0.###e+00}.");

            Assert.True(
                Math.Abs(dense - oracle) <= Tolerance,
                $"{templateName} k=[{string.Join(",", k)}] j={j}: dense {dense:0.###############} " +
                $"vs oracle {oracle:0.###############}, difference {Math.Abs(dense - oracle):0.###e+00}.");
        }
    }

    /// <summary>The tables reproduce the sums they stand for, by direct summation.</summary>
    [Fact]
    public void TablesMatchDirectSummation()
    {
        const int N = 13;
        double[] w = TemplateValidator.NoisyCircle(N, 7);
        var tables = new GoalTables(w);

        var random = new Random(11);
        for (int trial = 0; trial < 200; trial++)
        {
            int s = random.Next(N);
            int start = random.Next(N);
            int length = 1 + random.Next(N);

            double anti = 0;
            double diagonal = 0;
            for (int i = start; i < start + length; i++)
            {
                anti += tables.X[i] * tables.Y[GoalTables.Mod(s - i, N)];
                diagonal += tables.X[i] * tables.Y[(i + s) % N];
            }

            // Lane 1 is the x against y product.
            Assert.Equal(anti, tables.Anti(s, start + length, 1) - tables.Anti(s, start, 1), 1e-12);
            Assert.Equal(diagonal, tables.Diag(s, start + length, 1) - tables.Diag(s, start, 1), 1e-12);
        }
    }

    /// <summary>The table memory stays inside the budget of SPEC §6.3.</summary>
    [Fact]
    public void TableMemoryIsAsDocumented()
    {
        double[] w = TemplateValidator.NoisyCircle(120, 1);
        var tables = new GoalTables(w);

        // SPEC §6.3 quotes about 1.9 MB per orientation at n = 120.
        Assert.InRange(tables.ByteCount, 1_800_000, 2_100_000);
    }

    /// <summary>The distance computed from an explicit basis (SPEC §6.2).</summary>
    /// <param name="basis">The basis.</param>
    /// <param name="w">The goal coordinate vector.</param>
    /// <param name="j">The offset.</param>
    /// <returns>The error.</returns>
    private static double DenseError(TileBasis basis, double[] w, int j)
    {
        int n = w.Length / 2;
        var shifted = new double[2 * n];
        for (int t = 0; t < n; t++)
        {
            int index = (t + j) % n;
            shifted[t] = w[index];
            shifted[n + t] = w[n + index];
        }

        return TileFit.Fit(basis, shifted).Error;
    }

    /// <summary>
    /// The oracle of SPEC §8.4: the basis comes from the singular value decomposition of
    /// the relation matrix, and the largest eigenvalue of B transposed times V times B is
    /// found with the symmetric eigenvalue solver of MathNet.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="w">The goal coordinate vector.</param>
    /// <param name="j">The offset.</param>
    /// <returns>The error.</returns>
    private static double OracleError(TileLayout layout, double[] w, int j)
    {
        int n = w.Length / 2;
        DenseMatrix relations = RelationMatrix.AllRelations(layout);

        Matrix<double> a = Matrix<double>.Build.Dense(
            relations.Rows, relations.Columns, (r, c) => relations[r, c]);

        // The right singular vectors of the vanishing singular values span the null space.
        var svd = a.Svd(computeVectors: true);
        Vector<double> singular = svd.S;
        Matrix<double> vt = svd.VT;

        var columns = new List<Vector<double>>();
        for (int i = 0; i < vt.RowCount; i++)
        {
            double value = i < singular.Count ? singular[i] : 0.0;
            if (value <= 1e-9)
            {
                columns.Add(vt.Row(i));
            }
        }

        Matrix<double> basis = Matrix<double>.Build.DenseOfColumnVectors(columns);

        // The goal, shifted, and the same goal turned by minus ninety degrees.
        var shifted = Vector<double>.Build.Dense(2 * n);
        var rotated = Vector<double>.Build.Dense(2 * n);
        for (int t = 0; t < n; t++)
        {
            int index = (t + j) % n;
            shifted[t] = w[index];
            shifted[n + t] = w[n + index];
            rotated[t] = w[n + index];
            rotated[n + t] = -w[index];
        }

        // V is w w^T for the euclidean measure and adds the rotated goal for Procrustes,
        // so that the largest eigenvalue of B^T V B is the lambda of SPEC §6.1.
        Matrix<double> v = shifted.OuterProduct(shifted);
        if (layout.Template.UsesProcrustes)
        {
            v += rotated.OuterProduct(rotated);
        }

        Matrix<double> reduced = basis.TransposeThisAndMultiply(v).Multiply(basis);

        // Symmetrise to remove the rounding asymmetry before the eigenvalue solver.
        reduced = (reduced + reduced.Transpose()) / 2.0;

        double lambda = 0;
        foreach (System.Numerics.Complex eigenvalue in reduced.Evd(Symmetricity.Symmetric).EigenValues)
        {
            lambda = Math.Max(lambda, eigenvalue.Real);
        }

        return Math.Max(0, shifted.DotProduct(shifted) - lambda);
    }
}
