using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The status document must not contradict the task register.
///
/// `PROJECT-STATUS.md` said "25 项全部仍为计划状态，仅缺陷 1 已修复" long after 24 of them were
/// fixed, and listed C1/C2 as "已登记未修" after both were closed. A status file that understates
/// progress is as damaging as one that overstates it: it is what a reader uses to decide whether the
/// project is finished.
/// </summary>
public sealed class StatusDocumentTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Status() => File.ReadAllText(Path.Combine(Root(), "PROJECT-STATUS.md"));

    private static string Register() => File.ReadAllText(Path.Combine(Root(), "TASK-REGISTER.md"));

    /// <summary>How many of the 25 audit defects the register marks fixed.</summary>
    private static int FixedCount() =>
        Regex.Matches(Register(), @"^\| \d+ \|[^\n]*\| ✅ \|", RegexOptions.Multiline).Count;

    [Fact]
    public void TheStatusDoesNotClaimTheDefectsAreUnstarted()
    {
        var status = Status();

        Assert.DoesNotContain("全部仍为\"计划状态\"", status);
        Assert.DoesNotContain("它尚未开始实施", status);
    }

    [Fact]
    public void CandidatesAreNotListedAsUnfixed()
    {
        // C1 and C2 were closed and the table still said 已登记未修.
        Assert.DoesNotMatch(@"\| C1 \|[^\n]*已登记未修", Status());
        Assert.DoesNotMatch(@"\| C2 \|[^\n]*已登记未修", Status());
    }

    [Fact]
    public void TheRegisterCountIsStatedConsistently()
    {
        int fixedCount = FixedCount();

        Assert.True(fixedCount >= 20, $"register marks only {fixedCount} fixed — is the regex still right?");
        Assert.Contains("✅ 已修", Register());
    }

    [Fact]
    public void TheUnverifiedCapabilityIsStillDeclared()
    {
        // The one claim that must never be softened: L4 has not run.
        var status = Status();

        Assert.Contains("未执行", status);
        Assert.Contains("L4", status);
    }
}
