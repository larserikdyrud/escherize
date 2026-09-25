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

        // The lookup tables of SPEC §6.3, one set per orientation, built once for the
        // whole search.
        GoalTables[] tables = [new GoalTables(goal.W), new GoalTables(goal.WReversed)];

        var overall = new TopCandidates(options.RawCandidateCount);
        long totalEvaluations = 0;
        long completedChunks = 0;
        long totalChunks = 0;

        // The k vectors of a template are enumerated once and used for both orientations.
        // At n = 120 the largest template has millions of them, so materialising the list
        // twice would cost hundreds of megabytes against the budget of SPEC §12.
        var work = new List<(bool Reversed, int TypeIndex, TemplateSpec Template, int[][] KVectors)>();
        foreach ((int index, TemplateSpec template) in templates)
        {
            int[][] kVectors = [.. KVectorEnumerator.Enumerate(template, n, options.MinimumK)];
            if (kVectors.Length == 0)
            {
                continue;
            }

            foreach (bool reversed in (bool[])[false, true])
            {
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

            GoalTables goalTables = tables[reversed ? 1 : 0];

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

                    // One evaluator serves the whole chunk; a k vector is loaded into it
                    // rather than building a new one (SPEC §6.3, §0.6).
                    var evaluator = new FastEvaluator(TemplatePlan.For(template), goalTables);

                    for (int index = range.Item1; index < range.Item2; index++)
                    {
                        int[] k = kVectors[index];
                        evaluator.Load(BasisPlan.Create(TileLayout.Create(template, k)));

                        for (int j = 0; j < n; j++)
                        {
                            double error = evaluator.Evaluate(j);

                            // Almost every evaluation is far from the best seen so far, so
                            // the candidate is only built when it stands a chance.
                            if (local.IsWorthOffering(error))
                            {
                                local.Offer(new ScoredCandidate(
                                    new CandidateKey(typeIndex, k, j, reversed), error));
                            }
                        }

                        localEvaluations += n;
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

}
