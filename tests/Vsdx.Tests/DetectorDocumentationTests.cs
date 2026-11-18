using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The documented list of detector codes must match the codes the analyser can emit.
///
/// `docs/capability-boundaries.md` tells a reader which problems the engine will find. A table that
/// drifts from the code is worse than no table: it promises coverage that may not exist and hides
/// coverage that does.
///
/// Verified while writing it: I had `edge.through-node` unmarked (it is error), `page.margins`
/// unmarked (it is info), and the warning count wrong by one (14, not 15) because `spacing.too-tight`
/// appears twice in the source for two different comparisons.
/// </summary>
public sealed class DetectorDocumentationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static HashSet<string> CodesInCode()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Vsdx.Engine", "LayoutAnalyzer.cs"));

        return Regex.Matches(source, "Code = \"([a-z.\\-]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> CodesInDocs()
    {
        var doc = File.ReadAllText(
            Path.Combine(RepoRoot(), "docs", "capability-boundaries.md"));

        // Every code namespace the analyser emits. `container` was absent, so a newly added
        // `container.heading-collision` read as undocumented although the table listed it.
        return Regex.Matches(doc, "`((?:node|group|container|edge|label|text|row|spacing|page)\\.[a-z.\\-]+)`")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EveryDocumentedCodeExists()
    {
        var documented = CodesInDocs();
        var actual = CodesInCode();

        var phantom = documented.Where(c => !actual.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();

        Assert.True(phantom.Count == 0,
            $"documented but not emitted: {string.Join(", ", phantom)}");
    }

    [Fact]
    public void EveryEmittedCodeIsDocumented()
    {
        var documented = CodesInDocs();
        var actual = CodesInCode();

        var missing = actual.Where(c => !documented.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            $"emitted but undocumented: {string.Join(", ", missing)}");
    }

    [Fact]
    public void TheStatedCountMatches()
    {
        // The doc says "22 类问题"; if a check is added the number must move with it.
        int count = CodesInCode().Count;
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "capability-boundaries.md"));

        Assert.Contains($"{count} 类问题", doc);
        Assert.Contains($"合计 {count} 个代码", doc);
    }
}
