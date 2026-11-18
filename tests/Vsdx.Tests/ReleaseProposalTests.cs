using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The release record must describe the release that exists.
///
/// It said "无任何 tag" while `v0.9.0` was tagged, named the artifact `Vsdx.Engine.1.0.0.nupkg` when the
/// project produces `AgentVsdx.Engine.Tool.0.9.0.nupkg`, and listed the tag and version as unchecked in the
/// preparation list while its own "已完成" sentence below claimed both were done.
///
/// The self-contradiction is the part worth guarding against: a checklist and a prose summary can drift
/// apart in either direction, and a reader has no way to tell which half is current.
/// </summary>
public sealed class ReleaseProposalTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Proposal() =>
        File.ReadAllText(Path.Combine(Root(), "docs", "RELEASE-PROPOSAL.md"));

    /// <summary>Reads the version the build actually stamps.</summary>
    private static string Version()
    {
        var props = File.ReadAllText(Path.Combine(Root(), "Directory.Build.props"));
        var match = Regex.Match(props, "<Version>([^<]+)</Version>");

        Assert.True(match.Success, "Directory.Build.props declares no Version");

        return match.Groups[1].Value.Trim();
    }

    private static string PackageId()
    {
        var csproj = File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Cli", "Vsdx.Cli.csproj"));
        var match = Regex.Match(csproj, "<PackageId>([^<]+)</PackageId>");

        Assert.True(match.Success, "Vsdx.Cli.csproj declares no PackageId");

        return match.Groups[1].Value.Trim();
    }

    [Fact]
    public void TheNamedArtifactMatchesTheProject()
    {
        var proposal = Proposal();

        // The id and version it quotes must be the ones the build produces.
        Assert.Contains($"{PackageId()}.{Version()}.nupkg", proposal);
    }

    [Fact]
    public void ItDoesNotClaimTheVersionIsUndecided()
    {
        var proposal = Proposal();

        Assert.DoesNotContain("无任何 tag", proposal);
        Assert.DoesNotContain("<Version>1.0.0</Version>", proposal);
    }

    [Fact]
    public void TheChecklistAgreesWithTheProseSummary()
    {
        // If the prose says the tag and version are done, no checklist item may say otherwise.
        var proposal = Proposal();

        bool proseSaysDone = proposal.Contains("已完成：", StringComparison.Ordinal)
                          && proposal.Contains("tag", StringComparison.Ordinal);

        bool checklistDisagrees = proposal.Contains("- [ ] 版本号确认", StringComparison.Ordinal)
                               || proposal.Contains("- [ ] Git tag", StringComparison.Ordinal);

        Assert.False(proseSaysDone && checklistDisagrees,
            "the proposal marks release steps as done in one place and pending in another");
    }

    [Fact]
    public void L4IsStillTheStatedBlockerForOnePointOh()
    {
        var proposal = Proposal();

        Assert.Contains("L4", proposal);
        Assert.Contains("1.0.0", proposal);
        Assert.Contains("未", proposal);
    }

    [Fact]
    public void ItDoesNotCarryACountThatWillDrift()
    {
        // The test count in this file said 755 long after the suite had grown past 1000.
        var proposal = Proposal();

        var stale = Regex.Matches(proposal, @"(\d{3,})\s*测试通过").Select(m => m.Value).ToList();

        Assert.True(stale.Count == 0, "counts that will drift: " + string.Join(", ", stale));
    }
}
