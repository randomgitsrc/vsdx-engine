using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The real-world validation report must match what the engine currently produces.
///
/// The table records a score per scenario. Those scores move as detectors are added, so a written
/// number goes stale without anything failing — and I wrote two of them from memory before measuring:
/// the report said 84 and 80 where the engine returns 86 and 82.
///
/// This does not assert the scores are *good*, only that the document agrees with the engine.
/// </summary>
public sealed class RealworldValidationTests
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
        File.ReadAllText(Path.Combine(Root(), "docs", "phase8", "phase8.3-realworld-validation.md"));

    private static int Score(string scenario)
    {
        var path = Path.Combine(Root(), "examples", "realworld", $"{scenario}.ir.json");
        var ir = JsonSerializer.Deserialize<DiagramIr>(File.ReadAllText(path), Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-rw-{Guid.NewGuid():N}.vsdx");

        try
        {
            return new ProtocolRunner().Execute(ir, spec, output).Quality.Score;
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    [Theory]
    [InlineData("cbo")]
    [InlineData("deploy-topology")]
    [InlineData("user-data-flow")]
    [InlineData("checkout-sequence")]
    public void TheScoreInTheReportMatchesTheEngine(string scenario)
    {
        int score = Score(scenario);
        var report = Report();

        // The report uses Chinese names for the rows, not file names, so match on the score column of
        // the row whose description matches this scenario's file.
        var label = scenario switch
        {
            "cbo" => "跨境电商架构",
            "deploy-topology" => "部署拓扑",
            "user-data-flow" => "用户数据流",
            "checkout-sequence" => "下单时序",
            _ => scenario
        };

        // Only the visual-review section states current scores; earlier tables in this file record
        // historical runs (95/94/100/88) and are deliberately left as history.
        int sectionAt = report.LastIndexOf("目视复核", StringComparison.Ordinal);
        Assert.True(sectionAt > 0, "the visual-review section is missing");

        var row = report[sectionAt..].Split('\n')
            .FirstOrDefault(l => l.Contains(label, StringComparison.Ordinal) && l.StartsWith("| ", StringComparison.Ordinal));

        Assert.NotNull(row);
        Assert.Contains($"| {score} |", row);
    }

    [Fact]
    public void TheReportRecordsThatAVisualReviewHappened()
    {
        // The gap this section closes: twenty rounds of checking detectors without looking at a
        // rendered image, which is how a title painted over by a box survived a 98/100 score.
        Assert.Contains("目视复核", Report());
        Assert.Contains("PNG", Report());
    }

    [Fact]
    public void TheCorrectedJudgementIsRecorded()
    {
        // I read ~40% blank space into a diagram that measures 82% utilisation; the note explains why
        // the visual impression was wrong rather than quietly dropping the claim.
        var report = Report();

        Assert.Contains("利用率 82%", report);
        Assert.Contains("检测器是对的", report);
    }

    [Fact]
    public void EveryScenarioFileTheReportNamesExists()
    {
        foreach (var name in new[] { "cbo", "deploy-topology", "user-data-flow", "checkout-sequence" })
        {
            Assert.True(File.Exists(Path.Combine(Root(), "examples", "realworld", $"{name}.ir.json")),
                $"{name}.ir.json is missing");
        }
    }
}
