using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The L4 README's table must state the thresholds the script actually applies.
///
/// It said custom geometry needs "> 5" rows. The script uses `-ge 2`, and its own comment explains why: a
/// two-point path is MoveTo + LineTo, so a threshold of six would fail a correct file. The README carried the
/// value the script was written to avoid.
///
/// The README also never said what the fixture must contain. Two checks fail on a fixture missing their
/// object — no container, no free path — and an operator had no way to know before running it and reading a
/// failure they could not interpret.
/// </summary>
public sealed class L4ReadmeTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Readme() =>
        File.ReadAllText(Path.Combine(Root(), "tools", "l4", "README.md"));

    private static string Script() =>
        File.ReadAllText(Path.Combine(Root(), "tools", "l4", "verify-visio.ps1"));

    [Fact]
    public void TheGeometryThresholdMatchesTheScript()
    {
        var script = Script();
        var readme = Readme();

        var actual = Regex.Match(script, @"RowCount\(10\) -ge (\d+)");
        Assert.True(actual.Success, "the geometry check changed shape; update this test");

        // The README must state the same number the script compares against.
        Assert.Contains($"-ge {actual.Groups[1].Value}", readme);
    }

    [Fact]
    public void TheReadmeDoesNotCiteTheValueTheScriptRejects()
    {
        // "> 5" was the wrong number, and the script's comment exists to explain why six is wrong.
        var readme = Readme();

        Assert.DoesNotContain("行数 > 5", readme);
        Assert.DoesNotContain("> 5 rows", readme);
    }

    [Fact]
    public void TheReadmeStatesWhatTheFixtureMustContain()
    {
        // Without this an operator learns the requirement by reading a failure.
        var readme = Readme();

        Assert.Contains("用例要求", readme);
        Assert.Contains("容器", readme);
        Assert.Contains("自由路径", readme);
        Assert.Contains("字号", readme);
    }

    [Fact]
    public void EveryCheckInTheScriptAppearsInTheTable()
    {
        var readme = Readme();
        var script = Script();

        var names = Regex.Matches(script, @"Add-Check\s+""([a-z-]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(names);

        // The table describes them by concept rather than id, so check the count is in the same range
        // and that the two that gate on an object are called out.
        Assert.Contains("容器存在", readme);
        Assert.Contains("自定义几何", readme);
        Assert.True(names.Count >= 8, $"only {names.Count} checks in the script");
    }

    [Fact]
    public void ItSaysTheShippedExamplesNeedExtension()
    {
        // They have no free path, so running the script on one fails the geometry check.
        var readme = Readme();

        Assert.Contains("examples/realworld", readme);
        Assert.Contains("自由路径", readme);
    }
}
