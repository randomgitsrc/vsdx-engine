using System.Text.Json;
using System.Text.RegularExpressions;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The status report must describe the project that exists, and must not carry numbers it cannot keep true.
///
/// It is the file someone reads to decide whether to trust this repository, so a claim in it is worth more
/// than a claim in a phase record. Two things are checked: the scenarios it lists are the committed
/// examples with the scores they currently produce, and the counts it states match the engine.
///
/// I wrote "21 项检查" first — that is the number of `result.Check` call sites, not of checks. Several sit in
/// branches that cannot both run, and the engine reports 18. Same error as `grep -c` counting files instead
/// of matches: counting the thing that is easy to count rather than the thing being described.
/// </summary>
public sealed class StatusReportTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Report() =>
        File.ReadAllText(Path.Combine(Root(), "docs", "phase8", "status-2026-11.md"));

    [Fact]
    public void EveryScenarioItNamesIsACommittedExample()
    {
        var report = Report();
        var examples = Path.Combine(Root(), "examples", "realworld");

        var available = Directory.GetFiles(examples, "*.ir.json")
            .Select(f => Path.GetFileName(f).Replace(".ir.json", "", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        // Digits are part of scenario names: `netflix-active-active` has none, but the pattern must not
        // silently drop a name that does, or a real example goes unverified.
        var named = Regex.Matches(report, @"`([a-z][a-z0-9-]+)`")
            .Select(m => m.Groups[1].Value)
            .Where(available.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(available.Count, named.Count);
    }

    [Theory]
    [InlineData("cbo", 90)]
    [InlineData("user-data-flow", 100)]
    [InlineData("deploy-topology", 92)]
    [InlineData("checkout-sequence", 88)]
    public void TheStatedScoreIsWhatTheEngineProduces(string scenario, int expected)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(Path.Combine(Root(), "examples", "realworld", scenario + ".ir.json")), Json)!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-status-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);

            Assert.Equal(expected, result.Quality.Score);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TheStatedCheckCountMatchesTheEngine()
    {
        // 18, not the 21 `Check` call sites — some are alternatives that cannot both run.
        var vsdx = Path.Combine(Path.GetTempPath(), $"vsdx-status-c-{Guid.NewGuid():N}.vsdx");
        var model = Path.ChangeExtension(vsdx, ".json");

        try
        {
            File.WriteAllText(model, """
            {"id":"t","type":"architecture","pages":[{"id":"p","size":{"width":10,"height":8},
             "nodes":[{"id":"a","text":"A","bounds":{"x":3,"y":4,"w":1.3,"h":0.6}}],"edges":[]}]}
            """);

            new VsdxEngine().Create(model, vsdx, new WriteOptions { AutoLayout = false });

            int checks = new VsdxEngine().Validate(vsdx).Checks.Count;

            Assert.Equal(checks, ExtractCount(Report(), @"(\d+)\s*项 OPC"));
        }
        finally
        {
            foreach (var f in new[] { vsdx, model })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public void ItCarriesNoOtherHardcodedCounts()
    {
        // Scores are a snapshot and are labelled as one; everything else must point at a command.
        var report = Report();

        var loose = Regex.Matches(report, @"(?<![\w/])(\d{3,})\s*(项|个|条)")
            .Select(m => m.Value)
            .ToList();

        Assert.True(loose.Count == 0, "counts that will drift: " + string.Join(", ", loose));
    }

    [Fact]
    public void L4IsStatedAsAnOpenAssumption()
    {
        var report = Report();

        Assert.Contains("从未执行", report);
        Assert.Contains("假设", report);
    }

    private static int ExtractCount(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);

        Assert.True(match.Success, $"the report no longer states a count matching /{pattern}/");

        return int.Parse(match.Groups[1].Value);
    }
}
