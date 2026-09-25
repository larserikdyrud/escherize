using Escherize.Templates;

namespace Escherize.Search;

/// <summary>Options for the search (SPEC §7, §11).</summary>
public sealed record SearchOptions
{
    /// <summary>The templates to search, the <c>--types</c> flag. The default is all nine.</summary>
    public IReadOnlyList<string> Types { get; init; } = TemplateLibrary.Names;

    /// <summary>The number of candidates to report, the <c>--top</c> flag.</summary>
    public int TopK { get; init; } = 20;

    /// <summary>The lower bound on every k, the <c>--min-k</c> flag (SPEC §5.6).</summary>
    public int MinimumK { get; init; }

    /// <summary>
    /// The Procrustes distance below which a candidate counts as a duplicate of one
    /// already chosen, the <c>--diversity</c> flag (SPEC §7.3).
    /// </summary>
    public double Diversity { get; init; } = 0.02;

    /// <summary>
    /// The smallest acceptable relative neck width, the <c>--min-neck</c> flag. Zero
    /// disables the filter (SPEC §7.3).
    /// </summary>
    public double MinimumNeckWidth { get; init; }

    /// <summary>The degree of parallelism, the <c>--threads</c> flag (SPEC §7.1).</summary>
    public int Threads { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// The number of raw candidates kept before post processing, max(10 K, 200)
    /// (SPEC §7.2).
    /// </summary>
    public int RawCandidateCount => Math.Max(10 * TopK, 200);

    /// <summary>Resolves the template names to templates, keeping the table order.</summary>
    /// <returns>The selected templates and their indices.</returns>
    /// <exception cref="ArgumentException">A name does not match a template.</exception>
    public List<(int Index, TemplateSpec Template)> ResolveTemplates()
    {
        var selected = new List<(int, TemplateSpec)>();
        for (int i = 0; i < TemplateLibrary.All.Count; i++)
        {
            TemplateSpec template = TemplateLibrary.All[i];
            foreach (string name in Types)
            {
                if (string.Equals(name, template.Name, StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add((i, template));
                    break;
                }
            }
        }

        if (selected.Count == 0)
        {
            throw new ArgumentException("No template matched the requested types.", nameof(Types));
        }

        return selected;
    }
}

/// <summary>What a completed search produced (SPEC §7).</summary>
/// <param name="Candidates">The final candidates, best first.</param>
/// <param name="RawCandidates">The raw candidates before post processing (SPEC §7.2).</param>
/// <param name="Evaluations">How many distance evaluations were performed.</param>
/// <param name="Elapsed">How long the search took.</param>
public sealed record SearchResult(
    IReadOnlyList<Candidate> Candidates,
    IReadOnlyList<ScoredCandidate> RawCandidates,
    long Evaluations,
    TimeSpan Elapsed);
