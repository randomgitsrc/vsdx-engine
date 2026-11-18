using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The README must describe the product that exists, and must lead with what is unverified.
///
/// It documented only `create`; `execute`, `canvas`, `theme.palette` and `typography` were absent,
/// so the entry point to the project did not mention the protocol path at all. It also said nothing
/// about L4, which is the single fact a prospective user most needs before adopting 0.9.0.
/// </summary>
public sealed class ReadmeTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Readme() => File.ReadAllText(Path.Combine(Root(), "README.md"));

    [Fact]
    public void ItStatesTheVerificationGapNearTheTop()
    {
        // Not buried: a reader must meet this before the feature list, not after it.
        var readme = Readme();
        int gapAt = readme.IndexOf("从未在 Microsoft Visio", StringComparison.Ordinal);
        int featuresAt = readme.IndexOf("## 交付示例", StringComparison.Ordinal);

        Assert.True(gapAt > 0, "the README does not state that L4 has not run");
        Assert.True(featuresAt < 0 || gapAt < featuresAt, "the verification gap is stated after the examples");
    }

    [Fact]
    public void ItNamesTheVersionAndWhyItIsNot1()
    {
        var readme = Readme();

        Assert.Contains("0.9.0", readme);
        Assert.Contains("prerelease", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("execute")]
    [InlineData("canvas")]
    [InlineData("palette")]
    [InlineData("minReadableSize")]
    public void ItDocumentsTheSpecPath(string term)
    {
        Assert.Contains(term, Readme());
    }

    [Fact]
    public void EveryLayoutItListsIsReal()
    {
        // `--layout ... swimlane` was missing while the strategy existed.
        var readme = Readme();

        foreach (var name in new[] { "layered", "flow", "tree", "grid", "swimlane", "manual" })
        {
            if (!readme.Contains(name, StringComparison.Ordinal)) continue;

            Assert.Contains(name, File.ReadAllText(
                Path.Combine(Root(), "src", "Vsdx.Engine", "LayoutStrategies.cs")));
        }

        Assert.Contains("swimlane", readme);
    }

    [Fact]
    public void TheDocsIndexCoversTheDocumentsItClaims()
    {
        var indexPath = Path.Combine(Root(), "docs", "README.md");
        Assert.True(File.Exists(indexPath), "docs/README.md does not exist");

        var index = File.ReadAllText(indexPath);

        foreach (var doc in new[] { "capability-boundaries.md", "limitations.md", "RELEASE-PROPOSAL.md" })
        {
            Assert.Contains(doc, index);
            Assert.True(File.Exists(Path.Combine(Root(), "docs", doc)), $"{doc} is linked but missing");
        }
    }

    [Fact]
    public void TheIndexCountsWhatItDescribes()
    {
        // "22 项候选" was written as 23 first; the register holds C1-C20, C22 and C23.
        var index = File.ReadAllText(Path.Combine(Root(), "docs", "README.md"));
        var register = File.ReadAllText(Path.Combine(Root(), "docs", "phase8", "phase8.3-candidates.md"));

        int entries = Regex.Matches(register, @"^## C\d+", RegexOptions.Multiline).Count;

        Assert.Contains($"{entries} 个条目", index);
    }
}
