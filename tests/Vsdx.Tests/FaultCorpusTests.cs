using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every fault case must actually produce the fault it is named for, and only be in the corpus if a test
/// uses it.
///
/// `BenchmarkTests.Fault_IsDetected` does assert the expected code is present, so the seven listed cases
/// are genuinely verified. Two problems sat outside that net:
///
/// `fault-cross-region` exists in `benchmarks/faults/` but is not in the expected-code dictionary, so no
/// test ever ran it. Its four nodes carry no `groupId` and the page has no containers, so
/// `edge.diagonal-across-band` — which requires a straight edge to clip a *third container* — cannot
/// possibly fire. The case has been dead since it was added.
///
/// The second is quieter: several cases also raise `page.margins` and `page.underused`. That does not
/// break the assertion (it checks the expected code is present, not that it is alone), but it means a
/// case could pass while its intended fault had stopped firing, if the incidental codes were mistaken for
/// the point.
/// </summary>
public sealed class FaultCorpusTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string FaultDir() => Path.Combine(Root(), "benchmarks", "faults");

    private static HashSet<string> CodesFrom(string faultFile)
    {
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-flt-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(faultFile, output, new WriteOptions { Layout = "manual" });

            return LayoutAnalyzer.Analyze(new VsdxEngine().Read(output).Diagram)
                .Issues.Select(i => i.Code)
                .ToHashSet(StringComparer.Ordinal);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    /// <summary>The mapping the benchmark test uses, duplicated here so a change breaks both.</summary>
    private static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["fault-overlap"] = "node.overlap",
        ["fault-container-too-small"] = "group.member-escapes",
        ["fault-long-text"] = "text.overflow-horizontal",
        ["fault-out-of-page"] = "node.out-of-page",
        ["fault-crossing"] = "edge.crossing",
        ["fault-misaligned"] = "row.misaligned",
        ["fault-size-mix"] = "row.size-jitter"
    };

    [Fact]
    public void EveryFaultCaseProducesItsNamedFault()
    {
        var failures = new List<string>();

        foreach (var (name, code) in Expected)
        {
            var file = Path.Combine(FaultDir(), name + ".json");
            if (!File.Exists(file)) { failures.Add($"{name}: file missing"); continue; }

            var codes = CodesFrom(file);

            if (!codes.Contains(code)) failures.Add($"{name}: expected {code}, got [{string.Join(",", codes)}]");
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    [Fact]
    public void EveryFaultFileIsUsedByATest()
    {
        // A case nobody runs is worse than no case: it looks like coverage.
        var orphans = Directory.GetFiles(FaultDir(), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && !Expected.ContainsKey(n!))
            .ToList();

        Assert.True(orphans.Count == 0,
            "fault cases with no test asserting them: " + string.Join(", ", orphans));
    }

    [Fact]
    public void AFaultCaseIsNotPassingOnIncidentalCodes()
    {
        // `page.margins` and `page.underused` appear in most cases. If one of those ever became the
        // expected code, the case would still pass while its real fault had gone.
        var incidental = new[] { "page.margins", "page.underused" };

        foreach (var (name, code) in Expected)
            Assert.DoesNotContain(code, incidental);
    }
}
