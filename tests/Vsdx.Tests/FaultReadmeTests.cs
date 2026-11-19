using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The fault directory needs a README that says what each case is for and how to run it.
///
/// Eleven cases sat in a folder with no explanation. `FaultCorpusTests` maps each to its expected code, but
/// that mapping lives in test source — a caller who hit `edge.through-node` had to read the detector or the
/// test to learn which file demonstrates it, and running a case without `--layout manual` silently repairs
/// the fault it was built to show.
///
/// The README also carries the four construction traps, because each is a case where the obvious fixture does
/// not produce the fault: containers resize to their members, the router routes around obstacles, text boxes
/// grow to fit, and auto-layout pulls nodes back onto the page.
/// </summary>
public sealed class FaultReadmeTests
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
        File.ReadAllText(Path.Combine(Root(), "benchmarks", "faults", "README.md"));

    [Fact]
    public void EveryFaultFileAppearsInTheTable()
    {
        var readme = Readme();

        var files = Directory.GetFiles(Path.Combine(Root(), "benchmarks", "faults"), "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();

        Assert.NotEmpty(files);

        var missing = files.Where(f => !readme.Contains(f, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0, "fault cases not described: " + string.Join(", ", missing));
    }

    [Fact]
    public void ItSaysToUseManualLayout()
    {
        // Without it the faults are repaired by the layout pass and the case proves nothing.
        var readme = Readme();

        Assert.Contains("--layout manual", readme);
        Assert.Contains("自动布局会修正", readme);
    }

    [Fact]
    public void ItNamesTheConstructionTraps()
    {
        // The four where the obvious fixture does not produce the fault.
        var readme = Readme();

        Assert.Contains("autoSize", readme);
        Assert.Contains("waypoints", readme);
        Assert.Contains("bounds", readme);
    }

    [Fact]
    public void TheLevelsMatchTheAnalyser()
    {
        var readme = Readme();
        var source = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Engine", "LayoutAnalyzer.cs"));

        // Every code in the table must exist in the analyser.
        var codes = Regex.Matches(readme, @"`([a-z]+\.[a-z-]+)`")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(c => !c.Contains("layout.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(codes);

        var unknown = codes.Where(c => !source.Contains($"\"{c}\"", StringComparison.Ordinal)).ToList();

        Assert.True(unknown.Count == 0, "codes in the README the analyser never emits: " + string.Join(", ", unknown));
    }

    [Fact]
    public void ItExplainsTheDifferenceFromTheBenchmarks()
    {
        // Two directories with the same file shape and opposite purposes.
        var readme = Readme();

        Assert.Contains("正常图", readme);
        Assert.Contains("坏图", readme);
    }
}
