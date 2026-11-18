using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Verification levels and product capabilities must not share a count.
///
/// The matrix listed L1, L2, L3 and L4 among the capability rows, so "81 verified" counted three
/// verification stages as if they were capabilities. Worse, L1–L3 marked ✅ next to L4 marked ⛔ invites
/// exactly the wrong reading: that the project is three-quarters proven and only the last box is
/// unticked, when in fact the one unverified level is the one the product definition depends on.
/// </summary>
public sealed class VerificationLevelTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Matrix() =>
        File.ReadAllText(Path.Combine(Root(), "docs", "phase8", "capability-matrix.md"));

    [Fact]
    public void TheyHaveTheirOwnSection()
    {
        var matrix = Matrix();

        Assert.Contains("### 9a. 验证层级", matrix);
        Assert.Contains("不是能力项", matrix);
    }

    [Fact]
    public void TheSectionSaysTheyAreExcludedFromTheCount()
    {
        Assert.Contains("不计入本表", Matrix());
    }

    [Fact]
    public void L4IsStatedAsNotExecutedRatherThanOutOfScope()
    {
        // "⛔ Not in Engine Scope" is the legend's meaning elsewhere; L4 is in scope and unexecuted,
        // and those are different facts with different consequences.
        var matrix = Matrix();
        int sectionAt = matrix.IndexOf("### 9a.", StringComparison.Ordinal);

        Assert.True(sectionAt > 0);
        var section = matrix[sectionAt..matrix.IndexOf("### 9b.", sectionAt, StringComparison.Ordinal)];

        Assert.Contains("未执行", section);
        Assert.DoesNotContain("⛔", section);
    }

    [Fact]
    public void TheSectionExplainsWhyTheOtherLevelsCannotSubstitute()
    {
        // The heart of the claim: passing L1-L3 says nothing about whether Visio opens the file.
        var matrix = Matrix();
        int sectionAt = matrix.IndexOf("### 9a.", StringComparison.Ordinal);
        var section = matrix[sectionAt..matrix.IndexOf("### 9b.", sectionAt, StringComparison.Ordinal)];

        Assert.Contains("不能替代", section);
        Assert.Contains("Microsoft Visio", section);
    }
}
