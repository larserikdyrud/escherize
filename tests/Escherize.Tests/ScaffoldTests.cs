using Xunit;

namespace Escherize.Tests;

/// <summary>
/// Phase F0 smoke tests: the solution builds, the projects reference each other and the
/// test runner works. Real coverage starts in phase F1 (SPEC §8.1).
/// </summary>
public sealed class ScaffoldTests
{
    [Fact]
    public void CoreIsReferenced()
    {
        Assert.Equal("Escherize", EscherizeInfo.Name);
        Assert.Equal("arXiv:1912.09605", EscherizeInfo.Reference);
    }

    [Fact]
    public void ImagingIsReferenced()
    {
        Assert.Equal(128, Imaging.ImagingInfo.AlphaBackgroundThreshold);
    }

    [Fact]
    public void UsageTextMentionsEveryCommand()
    {
        var writer = new StringWriter();
        Cli.Program.WriteUsage(writer);
        string usage = writer.ToString();

        Assert.Contains("preprocess", usage, StringComparison.Ordinal);
        Assert.Contains("run", usage, StringComparison.Ordinal);
        Assert.Contains("render", usage, StringComparison.Ordinal);
    }
}
