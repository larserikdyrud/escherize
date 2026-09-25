namespace Escherize.Templates;

/// <summary>
/// Enumerates the ways the n - nv interior points can be spread over the k variables of a
/// template (SPEC §5.6). The order is lexicographic and therefore deterministic.
/// </summary>
public static class KVectorEnumerator
{
    /// <summary>
    /// Enumerates every k vector with sum of m_v k_v equal to n - nv and k_v at least
    /// <paramref name="minimumK"/>.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <param name="pointCount">The number of tile points, n.</param>
    /// <param name="minimumK">The lower bound per k variable, the <c>--min-k</c> flag.</param>
    /// <returns>The k vectors, in lexicographic order. Each one is a fresh array.</returns>
    public static IEnumerable<int[]> Enumerate(TemplateSpec template, int pointCount, int minimumK = 0)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumK);

        int[] multiplicities = template.KMultiplicities();
        int budget = pointCount - template.VertexCount;
        if (budget < 0)
        {
            yield break;
        }

        var current = new int[multiplicities.Length];
        foreach (int[] result in Extend(multiplicities, budget, minimumK, current, 0))
        {
            yield return result;
        }
    }

    /// <summary>The number of k vectors, without materialising them (SPEC §5.6).</summary>
    /// <param name="template">The template.</param>
    /// <param name="pointCount">The number of tile points, n.</param>
    /// <param name="minimumK">The lower bound per k variable.</param>
    /// <returns>The number of combinations.</returns>
    public static long Count(TemplateSpec template, int pointCount, int minimumK = 0)
    {
        ArgumentNullException.ThrowIfNull(template);

        int[] multiplicities = template.KMultiplicities();
        int budget = pointCount - template.VertexCount;
        return budget < 0 ? 0 : CountFrom(multiplicities, budget, minimumK, 0);
    }

    /// <summary>The recursive counterpart of <see cref="Count"/>.</summary>
    /// <param name="multiplicities">The multiplicity of every k variable.</param>
    /// <param name="remaining">The number of interior points still to place.</param>
    /// <param name="minimumK">The lower bound per k variable.</param>
    /// <param name="index">The k variable being chosen.</param>
    /// <returns>The number of completions.</returns>
    private static long CountFrom(int[] multiplicities, int remaining, int minimumK, int index)
    {
        if (index == multiplicities.Length)
        {
            return remaining == 0 ? 1 : 0;
        }

        int reserved = 0;
        for (int i = index + 1; i < multiplicities.Length; i++)
        {
            reserved += multiplicities[i] * minimumK;
        }

        long total = 0;
        for (int value = minimumK; (multiplicities[index] * value) + reserved <= remaining; value++)
        {
            total += CountFrom(multiplicities, remaining - (multiplicities[index] * value), minimumK, index + 1);
        }

        return total;
    }

    /// <summary>The recursive enumeration step.</summary>
    /// <param name="multiplicities">The multiplicity of every k variable.</param>
    /// <param name="remaining">The number of interior points still to place.</param>
    /// <param name="minimumK">The lower bound per k variable.</param>
    /// <param name="current">The partially filled k vector.</param>
    /// <param name="index">The k variable being chosen.</param>
    /// <returns>The completed k vectors.</returns>
    private static IEnumerable<int[]> Extend(
        int[] multiplicities,
        int remaining,
        int minimumK,
        int[] current,
        int index)
    {
        if (index == multiplicities.Length)
        {
            if (remaining == 0)
            {
                yield return (int[])current.Clone();
            }

            yield break;
        }

        int reserved = 0;
        for (int i = index + 1; i < multiplicities.Length; i++)
        {
            reserved += multiplicities[i] * minimumK;
        }

        for (int value = minimumK; (multiplicities[index] * value) + reserved <= remaining; value++)
        {
            current[index] = value;
            foreach (int[] result in Extend(
                multiplicities,
                remaining - (multiplicities[index] * value),
                minimumK,
                current,
                index + 1))
            {
                yield return result;
            }
        }

        current[index] = 0;
    }
}
