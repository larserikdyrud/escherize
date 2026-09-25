using System.Globalization;
using System.Text;
using Escherize.Parametrization;
using Escherize.Templates;
using Xunit;
using Xunit.Abstractions;

namespace Escherize.Tests;

/// <summary>
/// Enumerates the glide axis and rotation sign configurations of SPEC §8.2 and reports
/// which ones produce a real tiling. This is what decides D1 and D2 in DECISIONS.md.
/// </summary>
/// <param name="output">The xUnit output sink.</param>
public sealed class TemplateConfigurationDiscovery(ITestOutputHelper output)
{
    /// <summary>The number of goal points used by the validation (SPEC §8.2 step 1).</summary>
    private const int GoalPointCount = 36;

    /// <summary>The seeds each configuration is tried with during discovery.</summary>
    private static readonly int[] DiscoverySeeds = [1, 2, 3];

    /// <summary>
    /// Runs every configuration of every template and writes a report. At least one
    /// configuration must pass and at least one must fail, otherwise the check would not
    /// be discriminating (SPEC §8.2 step 5).
    /// </summary>
    [Fact]
    public void ReportConfigurationOutcomes()
    {
        var report = new StringBuilder();
        report.AppendLine("Template configuration discovery (SPEC §8.2)");
        report.AppendLine(CultureInfo.InvariantCulture, $"n = {GoalPointCount}, seeds = [{string.Join(", ", DiscoverySeeds)}]");
        report.AppendLine();

        int passedTotal = 0;
        int failedTotal = 0;

        foreach (TemplateSpec template in TemplateLibrary.All)
        {
            int md = DenseBasisBuilder.BuildVertexBasis(template).Columns;
            report.AppendLine(CultureInfo.InvariantCulture,
                $"{template.Name} ({template.Heesch})  nv = {template.VertexCount}, md = {md}, " +
                $"metric = {(template.UsesProcrustes ? "Procrustes" : "Euclid")}");

            foreach ((GlideAxis[] axes, double sign) in Configurations(template))
            {
                TemplateSpec configured = template.WithConfiguration(axes, sign);

                var outcomes = new List<string>();
                bool allPassed = true;
                foreach (int seed in DiscoverySeeds)
                {
                    TemplateValidationResult result = TemplateValidator.Validate(configured, GoalPointCount, seed);
                    if (!result.Passed)
                    {
                        allPassed = false;
                        outcomes.Add($"seed {seed}: {result.Reason}");
                        break;
                    }

                    outcomes.Add($"seed {seed}: ok ({result.TileCount} tiles)");
                }

                if (allPassed)
                {
                    passedTotal++;
                }
                else
                {
                    failedTotal++;
                }

                report.AppendLine(CultureInfo.InvariantCulture,
                    $"    {Describe(template, axes, sign),-28} {(allPassed ? "PASS" : "FAIL")}  {outcomes[^1]}");
            }

            report.AppendLine();
        }

        report.AppendLine(CultureInfo.InvariantCulture,
            $"Configurations that passed: {passedTotal}, that failed: {failedTotal}");
        output.WriteLine(report.ToString());

        Assert.True(passedTotal > 0, "No configuration produced a tiling, so the templates are wrong.");
        Assert.True(
            failedTotal > 0,
            "Every configuration passed, so the validation of SPEC §8.2 is not discriminating.");
    }

    /// <summary>
    /// Every configuration of a template: all axis combinations for its glide pairs, and
    /// both rotation signs (SPEC §5.2, §8.2).
    /// </summary>
    /// <param name="template">The template.</param>
    /// <returns>The configurations.</returns>
    public static IEnumerable<(GlideAxis[] Axes, double ThetaSign)> Configurations(TemplateSpec template)
    {
        ArgumentNullException.ThrowIfNull(template);

        int glidePairs = template.GlidePairCount;
        int combinations = 1 << glidePairs;
        double[] signs = template.HasRotationPairs ? [1.0, -1.0] : [1.0];

        foreach (double sign in signs)
        {
            for (int mask = 0; mask < combinations; mask++)
            {
                var axes = new GlideAxis[glidePairs];
                for (int i = 0; i < glidePairs; i++)
                {
                    axes[i] = (mask & (1 << i)) == 0 ? GlideAxis.X : GlideAxis.Y;
                }

                yield return (axes, sign);
            }
        }
    }

    /// <summary>A short label for a configuration.</summary>
    /// <param name="template">The template.</param>
    /// <param name="axes">The glide axes.</param>
    /// <param name="sign">The rotation sign.</param>
    /// <returns>The label.</returns>
    private static string Describe(TemplateSpec template, GlideAxis[] axes, double sign)
    {
        var parts = new List<string>();
        if (axes.Length > 0)
        {
            parts.Add("axes " + string.Join("", axes));
        }

        if (template.HasRotationPairs)
        {
            parts.Add(sign > 0 ? "theta +" : "theta -");
        }

        return parts.Count == 0 ? "(no free choice)" : string.Join(", ", parts);
    }
}
