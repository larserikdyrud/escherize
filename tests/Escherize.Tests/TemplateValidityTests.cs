using Escherize.Parametrization;
using Escherize.Templates;
using Xunit;

namespace Escherize.Tests;

/// <summary>
/// Template validity (SPEC §8.2). The configuration recorded in DECISIONS.md must tile the
/// plane for every one of the nine types, and the configurations rejected there must not.
/// </summary>
public sealed class TemplateValidityTests
{
    /// <summary>The number of goal points used by the validation (SPEC §8.2 step 1).</summary>
    private const int GoalPointCount = 36;

    /// <summary>The number of seeds each template is checked with (SPEC §8.2).</summary>
    private const int SeedCount = 20;

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
    /// The configuration that ships tiles the plane, for twenty independent seeds
    /// (SPEC §8.2).
    /// </summary>
    /// <param name="templateName">The template.</param>
    [Theory]
    [MemberData(nameof(TemplateNames))]
    public void ShippedConfigurationTilesThePlane(string templateName)
    {
        TemplateSpec template = TemplateLibrary.ByName(templateName);

        for (int seed = 1; seed <= SeedCount; seed++)
        {
            TemplateValidationResult result = TemplateValidator.Validate(template, GoalPointCount, seed);
            Assert.True(result.Passed, $"{templateName} failed at seed {seed}: {result.Reason}");
            Assert.True(
                result.WorstEdgeError <= TemplateValidator.EdgeCoverageTolerance,
                $"{templateName} seed {seed}: edge coverage error {result.WorstEdgeError}.");
        }
    }

    /// <summary>
    /// The configurations that DECISIONS.md rejects really do fail, so that the check is
    /// discriminating rather than vacuous (SPEC §8.2 step 5).
    /// </summary>
    /// <param name="templateName">The template.</param>
    /// <param name="axisNames">The glide axes of the rejected configuration.</param>
    /// <param name="thetaSign">The rotation sign of the rejected configuration.</param>
    [Theory]
    [InlineData("IH2", "XY", 1.0)]
    [InlineData("IH2", "YX", 1.0)]
    [InlineData("IH3", "XY", 1.0)]
    [InlineData("IH3", "YX", 1.0)]
    [InlineData("IH6", "XX", 1.0)]
    [InlineData("IH6", "YY", 1.0)]
    [InlineData("IH7", "", 1.0)]
    [InlineData("IH21", "", 1.0)]
    [InlineData("IH28", "", 1.0)]
    public void RejectedConfigurationsFail(string templateName, string axisNames, double thetaSign)
    {
        TemplateSpec template = TemplateLibrary.ByName(templateName);

        var axes = new GlideAxis[axisNames.Length];
        for (int i = 0; i < axisNames.Length; i++)
        {
            axes[i] = axisNames[i] == 'X' ? GlideAxis.X : GlideAxis.Y;
        }

        TemplateSpec rejected = template.WithConfiguration(axes, thetaSign);

        bool anyPassed = false;
        for (int seed = 1; seed <= 5; seed++)
        {
            if (TemplateValidator.Validate(rejected, GoalPointCount, seed).Passed)
            {
                anyPassed = true;
                break;
            }
        }

        Assert.False(
            anyPassed,
            $"{templateName} with axes '{axisNames}' and theta sign {thetaSign} was expected to fail but tiled.");
    }

    /// <summary>
    /// The equivalent configuration, which is the shipped one turned a quarter turn, also
    /// tiles. This records that the axis choice is a convention, not a constraint
    /// (SPEC §8.2 step 5).
    /// </summary>
    /// <param name="templateName">The template.</param>
    /// <param name="axisNames">The glide axes of the equivalent configuration.</param>
    [Theory]
    [InlineData("IH2", "YY")]
    [InlineData("IH3", "YY")]
    [InlineData("IH5", "Y")]
    [InlineData("IH6", "YX")]
    public void EquivalentConfigurationsAlsoTile(string templateName, string axisNames)
    {
        TemplateSpec template = TemplateLibrary.ByName(templateName);

        var axes = new GlideAxis[axisNames.Length];
        for (int i = 0; i < axisNames.Length; i++)
        {
            axes[i] = axisNames[i] == 'X' ? GlideAxis.X : GlideAxis.Y;
        }

        TemplateValidationResult result =
            TemplateValidator.Validate(template.WithConfiguration(axes, -1.0), GoalPointCount, seed: 1);

        Assert.True(result.Passed, $"{templateName} with axes '{axisNames}' did not tile: {result.Reason}");
    }

    /// <summary>The degrees of freedom of the vertex parametrisation, md, are as recorded (SPEC §5.3).</summary>
    /// <param name="templateName">The template.</param>
    /// <param name="expectedMd">The value recorded in DECISIONS.md.</param>
    [Theory]
    [InlineData("IH1", 8)]
    [InlineData("IH2", 7)]
    [InlineData("IH3", 7)]
    [InlineData("IH4", 10)]
    [InlineData("IH5", 8)]
    [InlineData("IH6", 8)]
    [InlineData("IH7", 6)]
    [InlineData("IH21", 6)]
    [InlineData("IH28", 6)]
    public void VertexDegreesOfFreedomAreAsRecorded(string templateName, int expectedMd)
    {
        TemplateSpec template = TemplateLibrary.ByName(templateName);
        Assert.Equal(expectedMd, DenseBasisBuilder.BuildVertexBasis(template).Columns);
    }

    /// <summary>The shipped table carries the axes and signs that DECISIONS.md records.</summary>
    [Fact]
    public void ShippedTableMatchesTheRecordedDecisions()
    {
        foreach (TemplateSpec template in TemplateLibrary.All)
        {
            foreach (EdgeSpec edge in template.Edges)
            {
                if (edge.Kind == EdgeKind.R)
                {
                    Assert.True(edge.ThetaDeg < 0, $"{template.Name} has a positive rotation angle.");
                }

                if (edge.Kind == EdgeKind.G)
                {
                    Assert.NotEqual(GlideAxis.None, edge.Axis);
                }
            }
        }

        // IH6 is the one template whose two glide pairs use different axes.
        TemplateSpec ih6 = TemplateLibrary.ByName("IH6");
        Assert.NotEqual(ih6.Edges[0].Axis, ih6.Edges[1].Axis);

        TemplateSpec ih2 = TemplateLibrary.ByName("IH2");
        Assert.Equal(ih2.Edges[1].Axis, ih2.Edges[4].Axis);
    }

    /// <summary>The metric of each template follows from whether it has glide pairs (SPEC §5.2).</summary>
    [Fact]
    public void ProcrustesTemplatesAreExactlyTheGlideTemplates()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal) { "IH2", "IH3", "IH5", "IH6" };
        foreach (TemplateSpec template in TemplateLibrary.All)
        {
            Assert.Equal(expected.Contains(template.Name), template.UsesProcrustes);
        }
    }
}
