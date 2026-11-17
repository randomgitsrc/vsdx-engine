using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// BenchmarkTests: the corpus under <c>benchmarks/</c> is the regression net for layout quality.
///
/// Two kinds of assertion:
///  * every benchmark case must be deliverable (hard constraints pass) after the pipeline runs
///  * every fault-injection case must be caught, and its specific code must appear
///
/// The second half matters more than the first: it proves the detector is sensitive, not merely
/// quiet.
/// </summary>
public sealed class BenchmarkTests
{
    private static string BenchmarksRoot()
    {
        // Walk up from the test binaries until the repository root is found.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "benchmarks")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "benchmarks");
    }

    private static IEnumerable<(string family, string caseName, string path)> Cases(string family)
    {
        var dir = Path.Combine(BenchmarksRoot(), family);
        if (!Directory.Exists(dir)) yield break;

        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            yield return (family, Path.GetFileNameWithoutExtension(file), file);
    }

    public static TheoryData<string, string, string> DeliveringCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var family in new[] { "architecture", "microservices", "workflow", "network", "org", "hybrid" })
        foreach (var (f, c, p) in Cases(family))
            data.Add(f, c, p);
        return data;
    }

    [Theory]
    [MemberData(nameof(DeliveringCases))]
    public void Benchmark_IsDeliverable(string family, string caseName, string path)
    {
        var dir = TestData.NewTempDir($"bench-{family}-{caseName}");
        var vsdx = Path.Combine(dir, "out.vsdx");

        var engine = new VsdxEngine();
        var result = engine.Create(path, vsdx, new WriteOptions
        {
            Layout = "auto",
            FitToText = true,
            FitPage = true,
            Recenter = true
        });

        Assert.Empty(result.Compatibility.Errors);

        var report = LayoutAnalyzer.Analyze(engine.Read(vsdx).Diagram);
        Assert.True(report.HardConstraintsPassed,
            $"{family}/{caseName} 未通过硬约束: " +
            string.Join("; ", report.Errors.Select(e => $"{e.Code}({e.Message})")));
    }

    [Theory]
    [MemberData(nameof(DeliveringCases))]
    public void Benchmark_RendersSuccessfully(string family, string caseName, string path)
    {
        var dir = TestData.NewTempDir($"bench-render-{family}-{caseName}");
        var vsdx = Path.Combine(dir, "out.vsdx");
        var png = Path.Combine(dir, "out.png");

        var engine = new VsdxEngine();
        engine.Create(path, vsdx, new WriteOptions { Layout = "auto", FitPage = true });

        var render = engine.Render(new RenderRequest
        {
            InputPath = vsdx, OutputPath = png, Format = "png", Dpi = 96
        });

        Assert.Single(render.Pages);
        Assert.True(new FileInfo(png).Length > 1000, "rendered PNG looks empty");
    }

    public static TheoryData<string, string, string, string> FaultCases()
    {
        var data = new TheoryData<string, string, string, string>();
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fault-overlap"] = "node.overlap",
            ["fault-container-too-small"] = "group.member-escapes",
            ["fault-long-text"] = "text.overflow-horizontal",
            ["fault-out-of-page"] = "node.out-of-page",
            ["fault-crossing"] = "edge.crossing",
            ["fault-misaligned"] = "row.misaligned",
            ["fault-size-mix"] = "row.size-jitter"
        };

        foreach (var (_, c, p) in Cases("faults"))
        {
            if (expected.TryGetValue(c, out var code))
                data.Add("faults", c, p, code);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(FaultCases))]
    public void Fault_IsDetected(string family, string caseName, string path, string expectedCode)
    {
        Assert.Equal("faults", family);
        var dir = TestData.NewTempDir($"fault-{caseName}");
        var vsdx = Path.Combine(dir, "out.vsdx");

        // Build without the layout pipeline: faults must be reported on the author's own geometry.
        var engine = new VsdxEngine();
        engine.Create(path, vsdx, new WriteOptions { Layout = "manual" });

        var report = LayoutAnalyzer.Analyze(engine.Read(vsdx).Diagram);

        Assert.Contains(report.Issues, i => i.Code == expectedCode);
    }

    [Fact]
    public void Fault_CriticalOnesAreHardFailures()
    {
        // Content that is invisible in the deliverable must gate delivery.
        var critical = new[] { "fault-overlap", "fault-out-of-page", "fault-container-too-small" };

        foreach (var name in critical)
        {
            var path = Path.Combine(BenchmarksRoot(), "faults", name + ".json");
            if (!File.Exists(path)) continue;

            var dir = TestData.NewTempDir($"fault-hard-{name}");
            var vsdx = Path.Combine(dir, "out.vsdx");
            var engine = new VsdxEngine();
            engine.Create(path, vsdx, new WriteOptions { Layout = "manual" });

            var report = LayoutAnalyzer.Analyze(engine.Read(vsdx).Diagram);
            Assert.False(report.HardConstraintsPassed, $"{name} 应判定为不可交付");
            Assert.Equal(0, report.Score);
        }
    }

    [Fact]
    public void Fault_RepairFixesOverlapCase()
    {
        var path = Path.Combine(BenchmarksRoot(), "faults", "fault-overlap.json");
        var dir = TestData.NewTempDir("fault-repair");
        var broken = Path.Combine(dir, "broken.vsdx");

        var engine = new VsdxEngine();
        engine.Create(path, broken, new WriteOptions { Layout = "manual" });

        var before = LayoutAnalyzer.Analyze(engine.Read(broken).Diagram);
        Assert.False(before.HardConstraintsPassed);

        // Apply the same relocation the repair loop uses.
        var model = engine.Read(broken).Diagram;
        DiagramSizer.Apply(model);
        DiagramLayoutEngine.Apply(model, new LayoutOptions(), LayoutStrategy.Layered);
        DiagramLayout.Apply(model);
        OrthogonalRouter.Route(model);

        var after = LayoutAnalyzer.Analyze(model);

        // The overlap must be gone. Page-utilisation findings are about the canvas, which a
        // relocation does not change, so they are excluded from this assertion.
        Assert.DoesNotContain(after.Errors, e => e.Code == "node.overlap");
        Assert.True(after.Score >= before.Score);
    }

    [Fact]
    public void Benchmark_CorpusCoversAllSixFamilies()
    {
        var root = BenchmarksRoot();
        foreach (var family in new[] { "architecture", "microservices", "workflow", "network", "org", "hybrid" })
        {
            var dir = Path.Combine(root, family);
            Assert.True(Directory.Exists(dir), $"缺少基准族 {family}");
            Assert.True(Directory.GetFiles(dir, "*.json").Length >= 3,
                $"{family} 至少需要 simple/medium/complex 三档");
        }

        Assert.True(Directory.GetFiles(Path.Combine(root, "faults"), "*.json").Length >= 6,
            "故障注入案例不足");
    }

    [Fact]
    public void Benchmark_AutoStrategyMatchesFamily()
    {
        // Each family should resolve to a strategy that suits it.
        var expectations = new (string family, string caseName, LayoutStrategy expected)[]
        {
            ("architecture", "medium", LayoutStrategy.Layered),
            ("network", "medium", LayoutStrategy.Layered),
            ("microservices", "medium", LayoutStrategy.Layered),
            ("org", "medium", LayoutStrategy.Tree),
            ("hybrid", "medium", LayoutStrategy.Layered)
        };

        foreach (var (family, caseName, expected) in expectations)
        {
            var path = Path.Combine(BenchmarksRoot(), family, caseName + ".json");
            if (!File.Exists(path)) continue;

            var diagram = DiagramJson.Load(path);
            Assert.Equal(expected, DiagramLayoutEngine.SuggestStrategy(diagram));
        }
    }
}
