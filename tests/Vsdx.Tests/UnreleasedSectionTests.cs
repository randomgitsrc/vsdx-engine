using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Work done after a release belongs under `[Unreleased]`, not folded into the shipped version's entry.
///
/// `v0.9.0` is a real tag. Seventy-seven commits followed it, including eight user-visible behaviour fixes —
/// heading clearance, paper centring, `metadata.created`, text-block role, constraint notes, severity levels,
/// paper overflow, master references — and none of them were mentioned in the changelog. Its "统计" table
/// still said 755 tests and 4 structures, which is what `v0.9.0` had and what the current tree does not.
///
/// Editing that table would have been worse than leaving it: the entry describes the tag, and a reader
/// comparing the two needs the number that belonged to the tag. It now says so, and points at commands for
/// the current values.
/// </summary>
public sealed class UnreleasedSectionTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Log() => File.ReadAllText(Path.Combine(Root(), "CHANGELOG.md"));

    [Fact]
    public void ThereIsAnUnreleasedSectionAndItComesFirst()
    {
        var log = Log();

        int unreleased = log.IndexOf("## [Unreleased]", StringComparison.Ordinal);
        int released = log.IndexOf("## [0.9.0]", StringComparison.Ordinal);

        Assert.True(unreleased > 0, "no [Unreleased] section");
        Assert.True(released > unreleased, "[Unreleased] does not precede the released entry");
    }

    [Fact]
    public void EveryUserVisibleFixIsMentioned()
    {
        // The eight that changed what a caller sees. If one is dropped from the section, the reader
        // upgrading from 0.9.0 has no way to learn it happened.
        var log = Log().Substring(0, Log().IndexOf("## [0.9.0]", StringComparison.Ordinal));

        foreach (var topic in new[]
                 {
                     "容器标题",        // heading clearance
                     "居中",            // paper centring
                     "paper",           // overflow checking
                     "requiresVisualReview",
                     "lossless",
                     "metadata",
                     "role",
                     "note",
                     "严重程度",
                     "Master"
                 })
            Assert.Contains(topic, log);
    }

    [Fact]
    public void TheReleasedStatsAreMarkedAsASnapshot()
    {
        // The table is correct for the tag and wrong for the tree; it has to say which.
        var log = Log();

        Assert.Contains("快照", log);
        Assert.Contains("v0.9.0", log);
    }

    [Fact]
    public void TheUnreleasedSectionDoesNotClaimANewVersion()
    {
        // Nothing has been tagged since 0.9.0, so nothing may claim to be 1.0.0 or a released 0.10.0.
        var log = Log();
        var head = log[..log.IndexOf("## [0.9.0]", StringComparison.Ordinal)];

        Assert.DoesNotContain("## [1.0.0]", head);
        Assert.DoesNotContain("## [0.10.0]", head);
    }

    [Fact]
    public void TheUnreleasedNotesAreBehaviourNotTestCounts()
    {
        // A changelog reader wants to know what changed for them. Test counts belong in the snapshot table.
        var log = Log();
        var head = log[..log.IndexOf("## [0.9.0]", StringComparison.Ordinal)];

        var counts = Regex.Matches(head, @"\d+\s*项通过").Count;

        Assert.Equal(0, counts);
    }
}
