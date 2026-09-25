using System.Collections.Concurrent;
using System.Diagnostics;
using Escherize.Parametrization;
using Escherize.Preprocessing;
using Escherize.Templates;

namespace Escherize.Search;

/// <summary>
/// The exhaustive search of SPEC §7.1: every template, every distribution of interior
/// points, every start offset and both orientations.
/// </summary>
public static class EscherizeSearch
{
    /// <summary>The number of k vectors handed to a worker at a time (SPEC §7.1).</summary>
    private const int ChunkSize = 64;

    /// <summary>Runs the search and post processing.</summary>
    /// <param name="goal">The normalised goal.</param>
    /// <param name="options">The search options.</param>
    /// <param name="progress">Receives the fraction completed, or null.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The result.</returns>
    public static SearchResult Run(
        GoalShape goal,
        SearchOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        List<ScoredCandidate> raw = Search(goal, options, progress, cancellationToken, out long evaluations);
        List<Candidate> candidates = CandidatePostProcessor.Process(goal, raw, options, cancellationToken);
        stopwatch.Stop();

        return new SearchResult(candidates, raw, evaluations, stopwatch.Elapsed);
    }

    /// <summary>Runs the raw search, without post processing (SPEC §7.1, §7.2).</summary>
    /// <param name="goal">The normalised goal.</param>
    /// <param name="options">The search options.</param>
    /// <param name="progress">Receives the fraction completed, or null.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <param name="evaluations">Receives the number of evaluations performed.</param>
    /// <returns>The raw candidates, best first.</returns>
    public static List<ScoredCandidate> Search(
        GoalShape goal,
        SearchOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        out long evaluations)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(options);

        int n = goal.PointCount;
        List<(int Index, TemplateSpec Template)> templates = options.ResolveTemplates();

        // The goal coordinates are stored twice so that a shift never needs a modulo,
        // once per orientation (SPEC §6.3).
        double[][] doubledX = [Doubled(goal.W, n), Doubled(goal.WReversed, n)];
        double[][] doubledY = [DoubledY(goal.W, n), DoubledY(goal.WReversed, n)];

        var overall = new TopCandidates(options.RawCandidateCount);
        long totalEvaluations = 0;
        long completedChunks = 0;
        long totalChunks = 0;

        var work = new List<(bool Reversed, int TypeIndex, TemplateSpec Template, int[][] KVectors)>();
        foreach (bool reversed in (bool[])[false, true])
        {
            foreach ((int index, TemplateSpec template) in templates)
            {
                int[][] kVectors = [.. KVectorEnumerator.Enumerate(template, n, options.MinimumK)];
                if (kVectors.Length == 0)
                {
                    continue;
                }

                work.Add((reversed, index, template, kVectors));
                totalChunks += (kVectors.Length + ChunkSize - 1) / ChunkSize;
            }
        }

        if (totalChunks == 0)
        {
            evaluations = 0;
            return [];
        }

        foreach ((bool reversed, int typeIndex, TemplateSpec template, int[][] kVectors) in work)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double[] x = doubledX[reversed ? 1 : 0];
            double[] y = doubledY[reversed ? 1 : 0];

            var collectors = new ConcurrentBag<TopCandidates>();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, options.Threads),
                CancellationToken = cancellationToken,
            };

            Parallel.ForEach(
                Partitioner.Create(0, kVectors.Length, ChunkSize),
                parallelOptions,
                () => new TopCandidates(options.RawCandidateCount),
                (range, _, local) =>
                {
                    long localEvaluations = 0;
                    for (int index = range.Item1; index < range.Item2; index++)
                    {
                        int[] k = kVectors[index];
                        TileBasis basis = DenseBasisBuilder.Build(template, k);
                        localEvaluations += EvaluateAllOffsets(basis, x, y, n, typeIndex, k, reversed, local);
                    }

                    Interlocked.Add(ref totalEvaluations, localEvaluations);
                    Interlocked.Increment(ref completedChunks);
                    progress?.Report(Math.Min(1.0, (double)Interlocked.Read(ref completedChunks) / totalChunks));
                    return local;
                },
                collectors.Add);

            foreach (TopCandidates collector in collectors)
            {
                overall.Merge(collector);
            }
        }

        evaluations = totalEvaluations;
        return overall.ToSortedList();
    }

    /// <summary>
    /// Evaluates every start offset for one basis, offering each result to the collector
    /// (SPEC §6.1, §6.2).
    /// </summary>
    /// <param name="basis">The tile basis.</param>
    /// <param name="doubledX">The doubled goal x coordinates.</param>
    /// <param name="doubledY">The doubled goal y coordinates.</param>
    /// <param name="n">The number of points.</param>
    /// <param name="typeIndex">The template index.</param>
    /// <param name="k">The k vector.</param>
    /// <param name="reversed">Whether the reversed goal is in use.</param>
    /// <param name="collector">The collector.</param>
    /// <returns>The number of evaluations performed.</returns>
    private static long EvaluateAllOffsets(
        TileBasis basis,
        double[] doubledX,
        double[] doubledY,
        int n,
        int typeIndex,
        int[] k,
        bool reversed,
        TopCandidates collector)
    {
        int m = basis.ColumnCount;
        bool procrustes = basis.Template.UsesProcrustes;

        var projection = new double[m];
        var rotatedProjection = new double[procrustes ? m : 0];

        for (int j = 0; j < n; j++)
        {
            basis.ProjectShifted(doubledX, doubledY, j, projection, rotatedProjection);

            // The goal has unit norm and a shift only permutes it, so ||w_j|| stays one.
            double error;
            if (procrustes)
            {
                double g11 = 0;
                double g22 = 0;
                double g12 = 0;
                for (int c = 0; c < m; c++)
                {
                    g11 += projection[c] * projection[c];
                    g22 += rotatedProjection[c] * rotatedProjection[c];
                    g12 += projection[c] * rotatedProjection[c];
                }

                double half = (g11 - g22) / 2;
                double lambda = ((g11 + g22) / 2) + Math.Sqrt((half * half) + (g12 * g12));
                error = 1.0 - lambda;
            }
            else
            {
                double squared = 0;
                for (int c = 0; c < m; c++)
                {
                    squared += projection[c] * projection[c];
                }

                error = 1.0 - squared;
            }

            collector.Offer(new ScoredCandidate(
                new CandidateKey(typeIndex, k, j, reversed),
                Math.Max(0, error)));
        }

        return n;
    }

    /// <summary>Repeats the x block of a coordinate vector so that shifts need no modulo.</summary>
    /// <param name="w">The coordinate vector of length 2n.</param>
    /// <param name="n">The number of points.</param>
    /// <returns>An array of length 2n holding the x coordinates twice.</returns>
    private static double[] Doubled(ReadOnlySpan<double> w, int n)
    {
        var result = new double[2 * n];
        for (int t = 0; t < n; t++)
        {
            result[t] = w[t];
            result[n + t] = w[t];
        }

        return result;
    }

    /// <summary>Repeats the y block of a coordinate vector.</summary>
    /// <param name="w">The coordinate vector of length 2n.</param>
    /// <param name="n">The number of points.</param>
    /// <returns>An array of length 2n holding the y coordinates twice.</returns>
    private static double[] DoubledY(ReadOnlySpan<double> w, int n)
    {
        var result = new double[2 * n];
        for (int t = 0; t < n; t++)
        {
            result[t] = w[n + t];
            result[n + t] = w[n + t];
        }

        return result;
    }
}
