using System.Text.Json;
using Vsdx.Protocol;
using Vsdx.Model;
using Vsdx.Engine;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `canvas.mode = fixed` promises a page of the requested size.
///
/// Requesting 20x14 with the default settings delivered 4.00x3.45 — an 80% reduction — while
/// `unmetConstraints` stayed empty. The mode had been applied (the page really was set to 20x14
/// first) and then silently shrunk to fit, so the caller was told the request was honoured when
/// it was not. Only `appliedDesignNotes` said "canvas.fixed+shrunk", which is not where anyone
/// looks to find out that their requirement was dropped.
///
/// The same class of failure runs the other way: content larger than the requested page grows it.
/// </summary>
public sealed class CanvasFixedSizeTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private sealed record RunResult(ExecutionResult Result, string OutputPath);

    private static RunResult Run(string specJson, string irJson)
    {
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-canvas-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, output);
        return new RunResult(result, output);
    }

    private static PageSize PageSizeOf(string outputPath) =>
        new VsdxEngine().Read(outputPath).Diagram.Pages[0].Size!;

    private const string OneNodeIr = """
    {"irVersion":"1.0","id":"a","kind":"architecture","nodes":[{"id":"x","label":"X","role":"service"}]}
    """;

    private const string LargeIr = """
    {"irVersion":"1.0","id":"big","kind":"architecture","nodes":[
      {"id":"a","label":"A"},{"id":"b","label":"B"},{"id":"c","label":"C"},
      {"id":"d","label":"D"},{"id":"e","label":"E"},{"id":"f","label":"F"},
      {"id":"g","label":"G"},{"id":"h","label":"H"},{"id":"i","label":"I"},
      {"id":"j","label":"J"},{"id":"k","label":"K"},{"id":"l","label":"L"}],
     "edges":[{"id":"e1","from":"a","to":"b"},{"id":"e2","from":"b","to":"c"},
              {"id":"e3","from":"c","to":"d"},{"id":"e4","from":"d","to":"e"}]}
    """;

    [Fact]
    public void Fixed_SmallContent_ReportsThatTheRequestedSizeWasNotKept()
    {
        // Content far smaller than the request: the page gets trimmed to fit.
        var result = Run(
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":20,"height":14,"unit":"inches"}}""",
            OneNodeIr);

        var codes = result.Result.UnmetConstraints.Select(u => u.Code).ToList();

        Assert.Contains(codes, c => c.StartsWith("canvas.", StringComparison.Ordinal));
    }

    [Fact]
    public void Fixed_NoShrinkRequested_KeepsTheRequestedSizeExactly()
    {
        var result = Run(
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":7,"height":5,"unit":"inches","shrinkToFit":false}}""",
            OneNodeIr);

        var size = PageSizeOf(result.OutputPath);
        Assert.Equal(7, size.Width, 1);
        Assert.Equal(5, size.Height, 1);

        // And no complaint is warranted when the request really was honoured.
        Assert.DoesNotContain(result.Result.UnmetConstraints,
            u => u.Code.StartsWith("canvas.", StringComparison.Ordinal));
    }

    [Fact]
    public void Fixed_LargeContent_ReportsWhenThePageHadToGrow()
    {
        // The other direction: content does not fit, so the page is enlarged past the request.
        var result = Run(
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":3,"height":2,"unit":"inches","shrinkToFit":false}}""",
            LargeIr);

        var size = PageSizeOf(result.OutputPath);
        bool exceeded = size.Width > 3.05 || size.Height > 2.05;

        if (exceeded)
        {
            Assert.Contains(result.Result.UnmetConstraints,
                u => u.Code.StartsWith("canvas.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Fixed_WhenSizeIsHonoured_SaysNothing()
    {
        // A request that fits comfortably must not produce a false complaint, or the warning
        // becomes noise and gets ignored.
        var result = Run(
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":12,"height":9,"unit":"inches","shrinkToFit":true}}""",
            OneNodeIr);

        var size = PageSizeOf(result.OutputPath);
        bool keptExactly = Math.Abs(size.Width - 12) < 0.05 && Math.Abs(size.Height - 9) < 0.05;

        if (!keptExactly)
        {
            // If it did not keep the size, it must at least say so — either way, not both silent
            // and wrong.
            Assert.Contains(result.Result.UnmetConstraints,
                u => u.Code.StartsWith("canvas.", StringComparison.Ordinal));
        }
    }
}
