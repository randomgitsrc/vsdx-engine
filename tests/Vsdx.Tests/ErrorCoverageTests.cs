using System.Text.RegularExpressions;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every error-level check — the seven that set `hardConstraintsPassed = false` — must have a fault case that
/// produces it on demand.
///
/// Three did not: `group.overlap`, `text.overflow-vertical` and `edge.through-node`. The first two were easy
/// to construct once the obstacle was understood — containers resize to their members unless `autoSize` is
/// false, and a text block needs enough lines to overflow its height. The third cannot be produced by the
/// router at all, because it routes around obstacles; it takes an explicit waypoint placed inside another
/// node, which is exactly the case a caller creates by writing their own route.
///
/// Without these, three of the seven failures a caller can trigger had no example showing what they look
/// like — and the detector tests exercised them only through hand-built models that are not part of the
/// shipped corpus.
/// </summary>
public sealed class ErrorCoverageTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Codes the analyser emits at `Severity = "error"`.</summary>
    private static List<string> ErrorCodes()
    {
        var source = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Engine", "LayoutAnalyzer.cs"));

        var positions = Regex.Matches(source, @"Code = ""([a-z.\-]+)""");

        var codes = new List<string>();

        for (int i = 0; i < positions.Count; i++)
        {
            int start = positions[i].Index;
            int end = i + 1 < positions.Count ? positions[i + 1].Index : source.Length;

            var block = source[start..Math.Min(end, start + 400)];

            if (Regex.IsMatch(block, @"Severity = ""error"""))
                codes.Add(positions[i].Groups[1].Value);
        }

        return codes.Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();
    }

    private static HashSet<string> CodesFromFaults()
    {
        var faults = Path.Combine(Root(), "benchmarks", "faults");
        var produced = new HashSet<string>(StringComparer.Ordinal);
        var engine = new VsdxEngine();

        foreach (var file in Directory.GetFiles(faults, "*.json"))
        {
            var output = Path.Combine(Path.GetTempPath(), $"vsdx-fault-{Guid.NewGuid():N}.vsdx");

            try
            {
                engine.Create(file, output, new WriteOptions { Layout = "manual" });

                foreach (var issue in LayoutAnalyzer.Analyze(engine.Read(output).Diagram).Issues)
                    produced.Add(issue.Code);
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }

        return produced;
    }

    [Fact]
    public void ThereAreSevenErrorLevelChecks()
    {
        // If this number moves, the coverage claim below is about a different set.
        Assert.Equal(7, ErrorCodes().Count);
    }

    [Fact]
    public void EveryErrorLevelCheckHasAFaultCase()
    {
        var uncovered = ErrorCodes().Where(c => !CodesFromFaults().Contains(c)).ToList();

        Assert.True(uncovered.Count == 0,
            "error-level checks with no fault case: " + string.Join(", ", uncovered));
    }

    [Fact]
    public void TheFaultDirectoryIsTheOnlyShippedSourceOfFailures()
    {
        // A caller who breaks a file wants an example of what that looks like. Anything error-level without a
        // case here is documented only in prose.
        var faults = Directory.GetFiles(Path.Combine(Root(), "benchmarks", "faults"), "*.json");

        Assert.True(faults.Length >= 7, $"only {faults.Length} fault cases");
    }
}
