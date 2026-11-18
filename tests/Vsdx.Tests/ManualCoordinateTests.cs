using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `layout=manual` must leave the author's coordinates where they were written.
///
/// The canvas block's auto branch calls `DiagramSizer.Recenter`, which shifts the page contents
/// without knowing whether the layout engine placed them or the author did. Measured: nodes authored
/// at (2, 9), (6, 9) and (10, 4) came back at (1.2, 5.8), (5.2, 5.8) and (9.2, 0.8) — a uniform
/// (-0.8, -3.2) shift — while `applied` reported only `canvas.auto`.
///
/// Manual's contract is the coordinates, so recentring violates it. The page may still grow to hold
/// the content; what it may not do is move the content.
/// </summary>
public sealed class ManualCoordinateTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string ManualIr = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "nodes":[
      {"id":"a","label":"A","anchor":{"mode":"fixed","x":2,"y":9}},
      {"id":"b","label":"B","anchor":{"mode":"fixed","x":6,"y":9}},
      {"id":"c","label":"C","anchor":{"mode":"fixed","x":10,"y":4}}],
     "edges":[{"id":"e1","from":"a","to":"b"},{"id":"e2","from":"b","to":"c"}]}
    """;

    private static (ExecutionResult Result, Diagram Diagram, string Path) Run(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(ManualIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-manual-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, path);
        return (result, new VsdxEngine().Read(path).Diagram, path);
    }

    private static Dictionary<string, (double X, double Y)> Positions(Diagram d) =>
        d.Pages[0].Nodes.ToDictionary(n => n.Id, n => (Math.Round(n.Bounds.X, 1), Math.Round(n.Bounds.Y, 1)),
            StringComparer.Ordinal);

    [Fact]
    public void ManualKeepsAuthoredCoordinates()
    {
        var (_, diagram, path) = Run("""{"specVersion":"1.0","layout":"manual"}""");
        try
        {
            var p = Positions(diagram);

            Assert.Equal((2.0, 9.0), p["a"]);
            Assert.Equal((6.0, 9.0), p["b"]);
            Assert.Equal((10.0, 4.0), p["c"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ManualDoesNotShiftEvenWhenTheContentIsOffCentre()
    {
        // Everything on one side of the sheet: a recentre would pull it to the middle.
        const string lopsided = """
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "nodes":[{"id":"a","label":"A","anchor":{"mode":"fixed","x":1.5,"y":1.5}},
                  {"id":"b","label":"B","anchor":{"mode":"fixed","x":3,"y":1.5}}],
         "edges":[{"id":"e","from":"a","to":"b"}]}
        """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(lopsided, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>("""{"specVersion":"1.0","layout":"manual"}""", Json)!;
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-manual2-{Guid.NewGuid():N}.vsdx");

        new ProtocolRunner().Execute(ir, spec, path);
        try
        {
            var diagram = new VsdxEngine().Read(path).Diagram;
            var p = Positions(diagram);

            Assert.Equal((1.5, 1.5), p["a"]);
            Assert.Equal((3.0, 1.5), p["b"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ThePageStillGrowsToHoldManualContent()
    {
        // Keeping coordinates must not mean letting content fall off the sheet.
        var (_, diagram, path) = Run("""{"specVersion":"1.0","layout":"manual"}""");
        try
        {
            var page = diagram.Pages[0];
            var size = page.Size!;

            foreach (var node in page.Nodes)
            {
                Assert.InRange(node.Bounds.X - node.Bounds.W / 2, -0.01, size.Width);
                Assert.InRange(node.Bounds.Y + node.Bounds.H / 2, -0.01, size.Height);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NonManualLayoutsStillRecentre()
    {
        // The auto path exists for a reason: a laid-out diagram should sit centred on its sheet.
        var ir = JsonSerializer.Deserialize<DiagramIr>("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
         "edges":[{"id":"e","from":"a","to":"b"}]}
        """, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>("""{"specVersion":"1.0","layout":"layered"}""", Json)!;
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-manual3-{Guid.NewGuid():N}.vsdx");

        new ProtocolRunner().Execute(ir, spec, path);
        try
        {
            var page = new VsdxEngine().Read(path).Diagram.Pages[0];
            var size = page.Size!;
            double left = page.Nodes.Min(n => n.Bounds.X - n.Bounds.W / 2);

            Assert.True(left > 0.1, $"laid-out content starts at x={left:F2}, expected a margin");
            Assert.True(left < size.Width / 2, "content should not sit in the right half");
        }
        finally { File.Delete(path); }
    }
}
