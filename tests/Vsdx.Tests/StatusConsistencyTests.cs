using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `PROJECT-STATUS.md` must not claim a defect is fixed and list it as outstanding at the same time.
///
/// It did: §5 opened with "25/25 已修复" and then carried a table headed "抽样复验仍存在（P0 级）" listing
/// three. The table had been captured against an older commit and never re-checked, so a reader got both
/// answers and no way to choose between them.
///
/// Re-measured on the current tree: the palette and fixed-canvas claims are genuinely fixed; the font one
/// was a misdiagnosis — the family is written to the model and read by the renderer, but the machine lacks
/// those fonts, and the renderer says so with `render.font-missing`.
/// </summary>
public sealed class StatusConsistencyTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Status() =>
        File.ReadAllText(Path.Combine(Root(), "PROJECT-STATUS.md"));

    [Fact]
    public void NoSectionListsDefectsAsBothFixedAndOutstanding()
    {
        var status = Status();

        bool claimsAllFixed = status.Contains("25/25 已修复", StringComparison.Ordinal);
        bool listsOutstanding = Regex.IsMatch(status, @"仍存在");

        Assert.False(claimsAllFixed && listsOutstanding,
            "the status file claims every defect is fixed and also lists some as outstanding");
    }

    [Fact]
    public void TheFontCaseIsDescribedAsAnEnvironmentLimit()
    {
        // The distinction matters: "renderer ignores the font" would be a defect to fix, "the machine has
        // no Georgia" is a condition to report — and the renderer does report it.
        var status = Status();

        Assert.Contains("render.font-missing", status);
        Assert.Contains("未安装", status);
    }

    [Fact]
    public void TheStatusCarriesNoHardcodedDate()
    {
        // A date in prose goes stale silently; a command pointer does not.
        var status = Status();

        Assert.DoesNotContain("2026-11-18", status);
        Assert.Contains("git log -1", status);
    }

    [Fact]
    public void TheMeasuredClaimsAreRecordedWithTheirMethod()
    {
        // Each re-measurement names how it was checked, so the next reader can repeat it.
        var status = Status();

        Assert.Contains("MD5", status);
        Assert.Contains("20.00×14.00", status);
    }
}
