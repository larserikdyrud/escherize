using Escherize.Parametrization;
using Escherize.Templates;
using MathNet.Numerics.LinearAlgebra;
using Xunit;

namespace Escherize.Tests;

/// <summary>Parametrisation tests (SPEC §8.3).</summary>
public sealed class ParametrizationTests
{
    /// <summary>The tolerance of the orthonormality check (SPEC §8.3).</summary>
    private const double OrthonormalityTolerance = 1e-10;

    /// <summary>The tolerance of the rank computation in the oracle (SPEC §8.3).</summary>
    private const double RankTolerance = 1e-9;

    /// <summary>Every template and a few k vectors, as test data.</summary>
    /// <returns>The template name and point count.</returns>
    public static TheoryData<string, int> TemplatesAndSizes()
    {
        var data = new TheoryData<string, int>();
        foreach (TemplateSpec template in TemplateLibrary.All)
        {
            foreach (int n in (int[])[24, 36, 48])
            {
                data.Add(template.Name, n);
            }
        }

        return data;
    }

    /// <summary>B has orthonormal columns, within 1e-10 (SPEC §8.3).</summary>
    /// <param name="templateName">The template.</param>
    /// <param name="pointCount">The number of tile points.</param>
    [Theory]
    [MemberData(nameof(TemplatesAndSizes))]
    public void BasisIsOrthonormal(string templateName, int pointCount)
    {
        TemplateSpec template = TemplateLibrary.ByName(templateName);

        foreach (int[] k in SampleKVectors(template, pointCount))
        {
            TileBasis basis = DenseBasisBuilder.Build(template, k);
            DenseMatrix gram = basis.Matrix.GramMatrix();

            for (int i = 0; i < gram.Rows; i++)
            {
                for (int j = 0; j < gram.Columns; j++)
                {
                    double expected = i == j ? 1.0 : 0.0;
                    Assert.True(
                        Math.Abs(gram[i, j] - expected) <= OrthonormalityTolerance,
                        $"{templateName} n={pointCount} k=[{string.Join(",", k)}]: " +
                        $"(B^T B)[{i},{j}] = {gram[i, j]}, expected {expected}.");
                }
            }
        }
    }

    /// <summary>
    /// Every column of B satisfies all relations of SPEC §5.1, including those on the
    /// interior points: A (B xi) = 0 (SPEC §8.3).
    /// </summary>
    /// <param name="templateName">The template.</param>
    /// <param name="pointCount">The number of tile points.</param>
    [Theory]
    [MemberData(nameof(TemplatesAndSizes))]
    public void BasisSatisfiesEveryRelation(string templateName, int pointCount)
    {
        TemplateSpec template = TemplateLibrary.ByName(templateName);
        var random = new Random(20260925);

        foreach (int[] k in SampleKVectors(template, pointCount))
        {
            TileLayout layout = TileLayout.Create(template, k);
            TileBasis basis = DenseBasisBuilder.Build(layout);
            DenseMatrix relations = RelationMatrix.AllRelations(layout);

            var xi = new double[basis.ColumnCount];
            for (int i = 0; i < xi.Length; i++)
            {
                xi[i] = (2 * random.NextDouble()) - 1;
            }

            double[] u = basis.ToCoordinates(xi);
            double[] residual = relations.Multiply(u);

            double worst = 0;
            foreach (double value in residual)
            {
                worst = Math.Max(worst, Math.Abs(value));
            }

            Assert.True(
                worst <= 1e-9,
                $"{templateName} n={pointCount} k=[{string.Join(",", k)}]: worst relation residual {worst}.");
        }
    }

    /// <summary>
    /// The number of columns of B equals the nullity of the full relation matrix, computed
    /// independently with a singular value decomposition (SPEC §8.3).
    /// </summary>
    /// <param name="templateName">The template.</param>
    /// <param name="pointCount">The number of tile points.</param>
    [Theory]
    [MemberData(nameof(TemplatesAndSizes))]
    public void BasisRankEqualsNullityOfTheRelations(string templateName, int pointCount)
    {
        TemplateSpec template = TemplateLibrary.ByName(templateName);

        foreach (int[] k in SampleKVectors(template, pointCount))
        {
            TileLayout layout = TileLayout.Create(template, k);
            TileBasis basis = DenseBasisBuilder.Build(layout);
            DenseMatrix relations = RelationMatrix.AllRelations(layout);

            Matrix<double> a = Matrix<double>.Build.Dense(
                relations.Rows,
                relations.Columns,
                (r, c) => relations[r, c]);

            int rank = 0;
            foreach (double singularValue in a.Svd(computeVectors: false).S)
            {
                if (singularValue > RankTolerance)
                {
                    rank++;
                }
            }

            int nullity = relations.Columns - rank;
            Assert.True(
                nullity == basis.ColumnCount,
                $"{templateName} n={pointCount} k=[{string.Join(",", k)}]: " +
                $"B has {basis.ColumnCount} columns but the relations have nullity {nullity}.");
        }
    }

    /// <summary>The vertex parametrisation has at least three degrees of freedom (SPEC §5.3).</summary>
    [Fact]
    public void EveryTemplateHasAtLeastThreeVertexDegreesOfFreedom()
    {
        foreach (TemplateSpec template in TemplateLibrary.All)
        {
            DenseMatrix vertexBasis = DenseBasisBuilder.BuildVertexBasis(template);
            Assert.True(vertexBasis.Columns >= 3, $"{template.Name} has md = {vertexBasis.Columns}.");
        }
    }

    /// <summary>The point count of a layout matches n = nv + sum of k_s (SPEC §3).</summary>
    [Fact]
    public void LayoutPointCountMatchesTheSpecification()
    {
        foreach (TemplateSpec template in TemplateLibrary.All)
        {
            foreach (int[] k in KVectorEnumerator.Enumerate(template, 30, minimumK: 1))
            {
                TileLayout layout = TileLayout.Create(template, k);
                int expected = template.VertexCount;
                for (int s = 0; s < template.VertexCount; s++)
                {
                    expected += layout.EdgeK(s);
                }

                Assert.Equal(expected, layout.PointCount);
                Assert.Equal(30, layout.PointCount);
            }
        }
    }

    /// <summary>The k enumeration is lexicographic, exhaustive and consistent with its own count (SPEC §5.6).</summary>
    [Fact]
    public void KEnumerationIsLexicographicAndComplete()
    {
        foreach (TemplateSpec template in TemplateLibrary.All)
        {
            int[][] vectors = [.. KVectorEnumerator.Enumerate(template, 36, minimumK: 1)];
            Assert.NotEmpty(vectors);
            Assert.Equal(KVectorEnumerator.Count(template, 36, minimumK: 1), vectors.Length);

            int[] multiplicities = template.KMultiplicities();
            foreach (int[] k in vectors)
            {
                int total = 0;
                for (int i = 0; i < k.Length; i++)
                {
                    total += multiplicities[i] * k[i];
                }

                Assert.Equal(36 - template.VertexCount, total);
            }

            for (int i = 1; i < vectors.Length; i++)
            {
                Assert.True(
                    Compare(vectors[i - 1], vectors[i]) < 0,
                    $"{template.Name}: k vectors {i - 1} and {i} are not in increasing order.");
            }
        }
    }

    /// <summary>The minimum k bound is honoured (SPEC §5.6).</summary>
    [Fact]
    public void KEnumerationHonoursTheMinimum()
    {
        TemplateSpec template = TemplateLibrary.ByName("IH4");
        foreach (int[] k in KVectorEnumerator.Enumerate(template, 40, minimumK: 3))
        {
            foreach (int value in k)
            {
                Assert.True(value >= 3);
            }
        }
    }

    /// <summary>Lexicographic comparison of two equally long vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>A negative value when the first sorts first.</returns>
    private static int Compare(int[] left, int[] right)
    {
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return left[i].CompareTo(right[i]);
            }
        }

        return 0;
    }

    /// <summary>
    /// A deterministic handful of k vectors for a template, so the tests cover several
    /// shapes without enumerating everything.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <param name="pointCount">The number of tile points.</param>
    /// <returns>Up to five k vectors.</returns>
    private static List<int[]> SampleKVectors(TemplateSpec template, int pointCount)
    {
        int[][] all = [.. KVectorEnumerator.Enumerate(template, pointCount, minimumK: 0)];
        Assert.NotEmpty(all);

        var chosen = new List<int[]>();
        int step = Math.Max(1, all.Length / 5);
        for (int i = 0; i < all.Length && chosen.Count < 5; i += step)
        {
            chosen.Add(all[i]);
        }

        // The last vector is included because it is the most lopsided one.
        chosen.Add(all[^1]);
        return chosen;
    }
}
