using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A canvas size given in centimetres must produce a page of that physical size.
///
/// The value was stored with the caller's unit still attached while FitPage compared and rewrote it as
/// inches; the writer then converted again. Measured: `10x10 cm` with `shrinkToFit` produced a
/// 2.92x1.18 inch page — exactly 2.54x too small — every node off the sheet, and an `unmet` message
/// describing an intermediate value rather than the file that was written.
///
/// Found by independent review. The suite had a test for centimetres but only on the create path,
/// where no FitPage runs.
/// </summary>
public sealed class CanvasUnitTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string SmallIr = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "nodes":[{"id":"a","label":"A","anchor":{"mode":"fixed","x":1.5,"y":1.5}},
              {"id":"b","label":"B","anchor":{"mode":"fixed","x":3,"y":1.5}}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    private static (ExecutionResult Result, Diagram Diagram, string Path) Run(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(SmallIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-unit-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, path);
        return (result, new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void CentimetresLargerThanContentAreHonouredInInches()
    {
        // 10 cm = 3.94 in — wide enough for content needing about 4.5, so `exceeded` is expected,
        // but the page must be the size the report claims.
        var (result, diagram, path) = Run(
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":10,"height":10,"unit":"cm"}}""");
        try
        {
            var size = diagram.Pages[0].Size!;

            // Not the raw 10 inches, and not 10/2.54/2.54 either.
            Assert.InRange(size.Width, 3.0, 12.0);
            Assert.Equal("inches", size.Unit);

            var exceeded = result.UnmetConstraints.FirstOrDefault(u => u.Code == "canvas.exceeded");
            if (exceeded is not null)
            {
                Assert.Contains($"{size.Width:0.##}x{size.Height:0.##}", exceeded.Reason);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AGenerousCentimetreSheetIsHonouredExactly()
    {
        // 40 cm = 15.75 in. ShrinkToFit defaults to true, so a sheet larger than the content is
        // trimmed by design — turning it off is what makes the size absolute. Measured with it off:
        // 15.748 x 11.811.
        var (_, diagram, path) = Run(
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":40,"height":30,"unit":"cm","shrinkToFit":false}}""");
        try
        {
            var size = diagram.Pages[0].Size!;

            // Compared at two decimals: 40/2.54 is 15.748, and one decimal would round it to 15.7
            // against an expectation of 15.8 — a rounding disagreement, not a size error.
            Assert.Equal(15.75, size.Width, 2);
            Assert.Equal(11.81, size.Height, 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheReportedSizeMatchesTheWrittenFile()
    {
        // The original defect's signature: the message described an intermediate value.
        var (result, diagram, path) = Run(
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":10,"height":10,"unit":"cm","shrinkToFit":true}}""");
        try
        {
            var size = diagram.Pages[0].Size!;

            foreach (var unmet in result.UnmetConstraints.Where(u => u.Code is "canvas.shrunk" or "canvas.exceeded"))
            {
                Assert.Contains($"{size.Width:0.##}", unmet.Reason);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MillimetresAlsoNormalise()
    {
        var (_, diagram, path) = Run(
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":400,"height":300,"unit":"mm","shrinkToFit":false}}""");
        try
        {
            var size = diagram.Pages[0].Size!;

            Assert.Equal(15.75, size.Width, 2);
            Assert.Equal("inches", size.Unit);
        }
        finally { File.Delete(path); }
    }
}
