using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `AGENTS.md` is the only file a new session loads automatically, so it carries two risks: it can
/// grow until it stops being read, and it can accumulate history that no reader needs.
///
/// The user-level rules ask that it state only the current situation and stay small — numbers that
/// change should be command pointers, not literals.
/// </summary>
public sealed class ProjectConventionsTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Agents() => File.ReadAllText(Path.Combine(Root(), "AGENTS.md"));

    [Fact]
    public void ItExists()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "AGENTS.md")));
    }

    [Fact]
    public void ItStaysWithinItsBudget()
    {
        // The workspace ceiling is 20 KB; this file is far smaller and should stay that way.
        Assert.True(Agents().Length < 6000, $"AGENTS.md is {Agents().Length} bytes");
    }

    [Fact]
    public void ItCarriesNoVolatileNumbers()
    {
        // Commit counts, test counts and line counts belong in commands, not prose — a literal rots
        // silently and is then believed.
        var agents = Agents();

        Assert.DoesNotMatch(@"(?<![\d.])\d{3,}\s*(项测试|个测试|测试通过)", agents);
    }

    [Fact]
    public void ItNamesTheBuildFlagThatPreventsStaleBinaries()
    {
        // The single most costly mistake in this project's verification loop.
        Assert.Contains("--no-incremental", Agents());
    }

    [Fact]
    public void ItWarnsThatAFailureMayBeStale()
    {
        // Two rounds were lost to chasing a failure that had already been fixed, or to believing a
        // stale binary. Re-running the one test after a full rebuild settles it in seconds.
        var agents = Agents();

        Assert.Contains("全量重建", agents);
        Assert.Contains("单独重跑", agents);
    }

    [Fact]
    public void ItDeclaresThatL4HasNotRun()
    {
        var agents = Agents();

        Assert.Contains("L4", agents);
        Assert.Contains("从未执行", agents);
    }

    [Fact]
    public void EveryDocumentItPointsAtExists()
    {
        var agents = Agents();

        foreach (Match m in Regex.Matches(agents, @"\[`([^`]+)`\]\(([^)]+)\)"))
        {
            var target = m.Groups[2].Value;

            if (target.StartsWith("http", StringComparison.Ordinal)) continue;
            if (target.StartsWith("#", StringComparison.Ordinal)) continue;

            var path = Path.Combine(Root(), target);
            Assert.True(File.Exists(path), $"AGENTS.md links '{target}', which does not exist");
        }
    }
}
