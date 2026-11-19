using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The L4 script cannot be run here, so it is only as good as its logic. This checks the properties that
/// survive without Visio: every stated check can fail, no check is skipped without saying so, and the script
/// covers each claim the engine makes.
///
/// **A check that could be skipped silently was the one real defect.** Two of them — `text-size-hierarchy`
/// and `text-content-not-empty` — sat inside `if ($textShapes.Count -ge 2)`. Given a one-block fixture the
/// script ran neither, reported PASS overall, and gave no indication it had checked less than it claimed.
/// Since the fixture is whatever `.vsdx` the operator passes in, that is not hypothetical.
///
/// The free-path block has the same shape but is fine: it prints `[INFO] 0 free path(s)` first, so the
/// absence is stated rather than hidden.
/// </summary>
public sealed class L4ScriptTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Script() =>
        File.ReadAllText(Path.Combine(Root(), "tools", "l4", "verify-visio.ps1"));

    [Fact]
    public void EveryCheckHasABooleanCondition()
    {
        // `Add-Check <name> (<condition>) <detail>` — the condition must be an expression, not a literal.
        var checks = Regex.Matches(Script(), @"Add-Check\s+""([a-z-]+)""\s+\(([^)]+)\)");

        Assert.NotEmpty(checks);

        foreach (Match m in checks)
        {
            var condition = m.Groups[2].Value.Trim();

            Assert.False(condition is "$true" or "1",
                $"check '{m.Groups[1].Value}' passes unconditionally");
        }
    }

    [Fact]
    public void TheTextChecksAreNotGatedOnHavingTwoBlocks()
    {
        // The specific regression: both sat inside `if ($textShapes.Count -ge 2)`.
        var script = Script();

        // Look at code lines only: the comment explaining the old bug names the guard it removed.
        var code = string.Join("\n", script.Split('\n').Where(l => !l.TrimStart().StartsWith("#")));

        int gate = code.IndexOf("if ($textShapes.Count -ge 2)", StringComparison.Ordinal);
        int contentCheck = code.IndexOf("text-content-not-empty", StringComparison.Ordinal);

        Assert.True(contentCheck > 0, "the text-content check is gone");

        Assert.False(gate > 0 && gate < contentCheck,
            "a text check is still behind the two-block guard, so a one-block fixture skips it silently");
    }

    [Fact]
    public void ASkippedCheckStillReports()
    {
        // Either it runs, or it fails with a reason — not neither.
        var script = Script();

        Assert.Contains("elseif ($textShapes.Count -ge 1)", script);
        Assert.Contains("a hierarchy needs at least two text blocks", script);
    }

    [Fact]
    public void ItCoversEveryClaimTheEngineMakes()
    {
        // The page names the areas L4 is meant to confirm; the script must touch each.
        var script = Script();

        foreach (var area in new[] { "text ", "free path", "z-order", "container" })
            Assert.Contains(area, script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItClosesVisioEvenOnFailure()
    {
        // A script that leaves VISIO.EXE running turns one bad run into a locked machine.
        var script = Script();

        Assert.Contains("$doc.Close()", script);
        Assert.Contains("$app.Quit()", script);
        Assert.Contains("finally", script);
    }

    [Fact]
    public void TheBracesBalance()
    {
        // Without PowerShell available this is the only structural check that the edit did not break it.
        var script = Script();

        Assert.Equal(script.Count(c => c == '{'), script.Count(c => c == '}'));
        Assert.Equal(script.Count(c => c == '('), script.Count(c => c == ')'));
    }
}
