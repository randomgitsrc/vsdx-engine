using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `known-issues.md` must list what is still open, and nothing that has been closed.
///
/// It had drifted the other way from most documents: two of its seven entries described problems fixed in
/// earlier rounds — container heading clearance and swimlane heading reservation — and still read as open.
/// A reader planning work from that file would have redone both.
///
/// Each entry now names the test that would fail if the fix were reverted, so the claim is checkable.
/// </summary>
public sealed class KnownIssuesTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Doc() =>
        File.ReadAllText(Path.Combine(Root(), "docs", "phase8", "known-issues.md"));

    [Fact]
    public void NoClosedIssueIsListedAsOpen()
    {
        var doc = Doc();

        // The two that were stale. Both are fixed and covered by the named tests.
        Assert.DoesNotContain("## 1. 容器标题与首行成员相接", doc);
        Assert.DoesNotContain("## 5. 泳道标题预留（本轮尝试失败，已回退）", doc);
    }

    [Fact]
    public void TheFixedOnesAreMentionedAsClosed()
    {
        // Removing them entirely would lose the history of what changed; the closing note keeps it
        // without implying they are still open.
        var doc = Doc();

        Assert.Contains("已完成的不在此列", doc);
        Assert.Contains("HeadingClearanceTests", doc);
        Assert.Contains("SwimlaneHeadingTests", doc);
    }

    [Fact]
    public void L4RemainsTheHeadlineOpenItem()
    {
        // The one entry that blocks the product's own definition must be first and unambiguous.
        var doc = Doc();
        int l4 = doc.IndexOf("L4 原生验证未执行", StringComparison.Ordinal);

        Assert.True(l4 > 0, "L4 is no longer listed");
        Assert.True(doc.IndexOf("## ", StringComparison.Ordinal) + 3 >= l4 - 3,
            "L4 is not the first open item");
    }

    [Fact]
    public void EveryOpenEntryStatesWhyItIsNotFixed()
    {
        // "Known" without a reason reads as an oversight. Each entry has to say what a fix would cost.
        var doc = Doc();
        // Only the numbered open entries; the closing note is not one of them.
        var sections = Regex.Split(doc, @"^## ", RegexOptions.Multiline)
            .Skip(1)
            .Where(s => Regex.IsMatch(s.Split('\n')[0], @"^\d+\."))
            .ToList();

        Assert.True(sections.Count > 0, "no numbered open entries found");

        foreach (var section in sections)
        {
            var title = section.Split('\n')[0];

            bool explains = section.Contains("为何未修", StringComparison.Ordinal)
                         || section.Contains("解除条件", StringComparison.Ordinal)
                         || section.Contains("若要改", StringComparison.Ordinal)
                         || section.Contains("若要", StringComparison.Ordinal)
                         || section.Contains("当前状态", StringComparison.Ordinal)
                         || section.Contains("详见", StringComparison.Ordinal);

            Assert.True(explains, $"entry '{title}' does not say why it is still open");
        }
    }
}
