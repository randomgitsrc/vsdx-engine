using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The three canvas modes must behave differently, and each must report what it did.
///
/// After finding that `paper` checked nothing, `fixed` was the obvious next suspect. It is sound — the
/// two modes differ in a way worth stating: `paper` is bounded by a standard sheet and cannot grow, so
/// overflow leaves shapes off the page; `fixed` grows the page to hold the content and reports the size
/// it actually needed. Neither is wrong, but a caller choosing between them needs to know which.
/// </summary>
public sealed class CanvasModeContractTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static (ExecutionResult Result, Diagram Written) Run(string irJson, string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-canvas-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);
            return (result, new VsdxEngine().Read(path).Diagram);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private const string Small = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    private static string Chain(int count)
    {
        var nodes = string.Join(",", Enumerable.Range(0, count)
            .Select(i => $$"""{"id":"n{{i}}","label":"N{{i}}"}"""));
        var edges = string.Join(",", Enumerable.Range(0, count)
            .Select(i => $$"""{"id":"e{{i}}","from":"n{{i}}","to":"n{{(i + 1) % count}}"}"""));

        return $$"""{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{{nodes}}],"edges":[{{edges}}]}""";
    }

    [Fact]
    public void FixedGrowsThePageRatherThanLeavingShapesOutside()
    {
        var (result, written) = Run(Chain(20),
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":10,"height":10}}""");

        var page = written.Pages[0];

        Assert.True(page.Size!.Height > 10, "fixed mode should have grown the page");
        Assert.All(page.Nodes, n => Assert.True(n.Bounds!.Y + n.Bounds.H / 2 <= page.Size.Height + 1e-6));

        var claim = result.UnmetConstraints.First(u => u.Code == "canvas.exceeded");
        Assert.Contains($"{page.Size.Width:0.##}x{page.Size.Height:0.##}", claim.Reason);
    }

    [Fact]
    public void FixedShrinksToTheContentWhenAskedAndAllowed()
    {
        var (result, written) = Run(Small,
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":40,"height":30,"shrinkToFit":true}}""");

        Assert.Contains(result.UnmetConstraints, u => u.Code == "canvas.shrunk");
        Assert.True(written.Pages[0].Size!.Width < 20, "shrinkToFit did not reduce the page");
    }

    [Fact]
    public void FixedWithoutShrinkKeepsTheRequestedSize()
    {
        var (_, written) = Run(Small,
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":40,"height":30,"shrinkToFit":false}}""");

        var size = written.Pages[0].Size!;

        Assert.Equal(40, size.Width, 1);
        Assert.Equal(30, size.Height, 1);
    }

    [Fact]
    public void PaperCannotGrowPastTheLargestSheet()
    {
        // The distinction between the modes, stated as a test: paper is bounded, so a diagram too
        // large for Tabloid keeps its shapes outside rather than expanding.
        var (result, written) = Run(Chain(20), """{"specVersion":"1.0","canvas":{"mode":"paper"}}""");

        Assert.True(written.Pages[0].Size!.Height <= 17.0, "paper must stay within a standard sheet");
        Assert.Contains(result.UnmetConstraints, u => u.Code is "canvas.exceeded" or "canvas.scaled");
    }

    [Fact]
    public void AutoFitsExactlyAndReportsNoSizeComplaint()
    {
        var (result, written) = Run(Small, """{"specVersion":"1.0","canvas":{"mode":"auto"}}""");

        Assert.Contains("canvas.auto", result.AppliedDesignNotes);
        Assert.DoesNotContain(result.UnmetConstraints, u => u.Code is "canvas.exceeded" or "canvas.shrunk");

        var page = written.Pages[0];
        Assert.All(page.Nodes, n => Assert.True(n.Bounds!.X + n.Bounds.W / 2 <= page.Size!.Width + 1e-6));
    }
}
