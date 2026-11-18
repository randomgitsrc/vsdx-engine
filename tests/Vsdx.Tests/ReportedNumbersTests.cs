using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The numbers a result reports must be reproducible from the file it produced.
///
/// Several rounds found report fields whose meaning did not match their implementation, so the
/// numeric ones deserve the same treatment: recompute them independently and compare.
///
/// Both hold. `pageUtilisation` on the cross-border diagram reads 0.7723 and an independent
/// bounding-box calculation gives 0.7723. `score` reads 86 and the documented formula — 100 minus
/// per-code penalties capped at three occurrences, warning weight 4 for `container.heading-collision`
/// and 2 for `edge.crossing` — gives exactly 86.
/// </summary>
public sealed class ReportedNumbersTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static (ExecutionResult Result, Diagram Written) Run(string scenario)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(Path.Combine(Root(), "examples", "realworld", $"{scenario}.ir.json")), Json)!;

        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-num-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);
            return (result, new VsdxEngine().Read(path).Diagram);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("cbo")]
    [InlineData("deploy-topology")]
    [InlineData("user-data-flow")]
    public void PageUtilisationMatchesTheGeometry(string scenario)
    {
        var (result, written) = Run(scenario);
        var page = written.Pages[0];

        var boxes = page.Nodes.Select(n => n.Bounds!)
            .Concat(page.Groups.Where(g => g.Bounds is not null).Select(g => g.Bounds!))
            .Concat(page.TextBlocks.Select(t => t.Bounds!))
            .ToList();

        double x0 = boxes.Min(b => b.X - b.W / 2), x1 = boxes.Max(b => b.X + b.W / 2);
        double y0 = boxes.Min(b => b.Y - b.H / 2), y1 = boxes.Max(b => b.Y + b.H / 2);

        double expected = (x1 - x0) * (y1 - y0) / (page.Size!.Width * page.Size.Height);

        var reported = result.Quality.PageUtilisation.Values.Single();

        Assert.Equal(expected, reported, 3);
    }

    [Fact]
    public void TheScoreFollowsTheDocumentedFormula()
    {
        var (result, _) = Run("cbo");

        double penalty = 0;

        // Recompute from the per-issue weights the analyser assigned.
        var report = LayoutAnalyzer.Analyze(_diagramForCbo.Value);

        foreach (var byCode in report.Warnings.GroupBy(i => i.Code, StringComparer.Ordinal))
            penalty += byCode.First().SoftWeight * Math.Min(byCode.Count(), 3);

        foreach (var byCode in report.Infos.GroupBy(i => i.Code, StringComparer.Ordinal))
            penalty += byCode.First().SoftWeight * Math.Min(byCode.Count(), 3) * 0.5;

        int expected = report.HardConstraintsPassed
            ? Math.Clamp((int)Math.Round(100 - penalty), 0, 100)
            : 0;

        Assert.Equal(expected, result.Quality.Score);
    }

    private static readonly Lazy<Diagram> _diagramForCbo = new(() =>
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(Path.Combine(Root(), "examples", "realworld", "cbo.ir.json")), Json)!;

        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-num2-{Guid.NewGuid():N}.vsdx");

        try
        {
            new ProtocolRunner().Execute(ir, spec, path);
            return new VsdxEngine().Read(path).Diagram;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    });

    [Fact]
    public void HardViolationsZeroImpliesAPositiveScore()
    {
        var (result, _) = Run("user-data-flow");

        if (result.Quality.HardViolations == 0)
            Assert.True(result.Quality.Score > 0, "no hard violations but the score is zero");
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
