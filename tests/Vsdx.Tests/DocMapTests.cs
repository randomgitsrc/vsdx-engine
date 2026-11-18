using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `docs/README.md` is the map, so it has to point at documents that exist and must not carry counts that
/// drift.
///
/// It said the capability matrix covers "106 项" — it has 124 — and that `phase8/` holds "34 个文件" — it
/// holds 38, and the number grows whenever a phase record is added. Both were plausible and both were
/// wrong; a count in prose has nothing keeping it true.
///
/// The map deliberately lists only ~13 of the 55 documents under `docs/`, because most of `phase8/` is
/// process record rather than reading material. That is a judgement, and it is stated in the file. What this
/// test checks is that the judgement has not quietly become an omission: every document a newcomer needs
/// must be reachable from here.
/// </summary>
public sealed class DocMapTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Map() => File.ReadAllText(Path.Combine(Root(), "docs", "README.md"));

    [Fact]
    public void EveryLinkResolves()
    {
        var root = Root();
        var broken = new List<string>();

        foreach (Match m in Regex.Matches(Map(), @"\]\(([^)]+)\)"))
        {
            var target = m.Groups[1].Value.Split('#')[0];

            if (target.Length == 0 || target.StartsWith("http", StringComparison.Ordinal)) continue;

            var path = Path.GetFullPath(Path.Combine(root, "docs", target));

            if (!File.Exists(path) && !Directory.Exists(path)) broken.Add(target);
        }

        Assert.True(broken.Count == 0, "broken links in the map: " + string.Join(", ", broken));
    }

    [Fact]
    public void TheDocumentsAReaderNeedsAreAllReachable()
    {
        // Not every file — the map says so itself — but every entry point.
        var map = Map();

        foreach (var essential in new[]
                 {
                     "capability-boundaries.md",
                     "limitations.md",
                     "api.md",
                     "installation.md",
                     "architecture.md",
                     "phase8/known-issues.md",
                     "phase8/capability-matrix.md",
                     "../skill/SKILL.md",
                     "../README.md"
                 })
            Assert.Contains(essential, map);
    }

    [Fact]
    public void TheMapCarriesNoHardcodedCounts()
    {
        // Numbers in prose go stale silently; the map should point at a command or at a table instead.
        var map = Map();

        var suspicious = Regex.Matches(map, @"(?<![\w/])(\d{2,})\s*(项|个文件|条|份)")
            .Select(m => m.Value)
            .ToList();

        Assert.True(suspicious.Count == 0,
            "the map states counts that will drift: " + string.Join(", ", suspicious));
    }

    [Fact]
    public void TheProcessRecordSectionExplainsItself()
    {
        // A reader who finds 38 files under phase8/ and 13 rows in the map needs to know why.
        var map = Map();

        Assert.Contains("过程记录", map);
        Assert.Contains("docs/phase8/*.md", map);
    }
}
