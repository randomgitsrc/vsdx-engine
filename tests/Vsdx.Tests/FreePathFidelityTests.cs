using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A free path's geometry and style must survive the round trip unchanged.
///
/// The IR declares rows as `{"type":"MoveTo","cells":{"X":"0","Y":"0"}}`; the model stores them under
/// `geometry[].rows[]` on a `FreePath`. Different shapes, same content — verified by comparing the actual
/// coordinates, not the keys.
///
/// My first check read `path.rows` on the model side, found zero, and looked like total data loss. The
/// rows were in `geometry[0].rows` all along. Reading a field that does not exist on that type is
/// indistinguishable from that field being empty, which is the same trap as the SVG text extraction two
/// rounds ago.
/// </summary>
public sealed class FreePathFidelityTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string WithPath = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "nodes":[{"id":"a","label":"A"}],"edges":[],
     "paths":[{"id":"p1","name":"折线","box":{"x":5,"y":5,"width":4,"height":2},
       "stroke":"#FF0000","strokeWidth":0.02,"fill":"#00FF00","filled":true,
       "rows":[{"type":"MoveTo","cells":{"X":"0","Y":"0"}},
               {"type":"LineTo","cells":{"X":"4","Y":"0"}},
               {"type":"LineTo","cells":{"X":"4","Y":"2"}},
               {"type":"LineTo","cells":{"X":"0","Y":"2"}}]}]}
    """;

    private static (Diagram Written, string Path) Run(string direction)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(WithPath, Json)!;
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(
            $$"""{"specVersion":"1.0","direction":"{{direction}}"}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-fp-{Guid.NewGuid():N}.vsdx");

        new ProtocolRunner().Execute(ir, spec, path);
        return (new VsdxEngine().Read(path).Diagram, path);
    }

    private static List<(string Type, Dictionary<string, string> Cells)> RowsOf(FreePath path) =>
        path.Geometry
            .SelectMany(section => section.Rows ?? new List<PathRow>())
            .Select(row => (row.Type ?? "", row.Cells ?? new Dictionary<string, string>()))
            .ToList();

    [Fact]
    public void TheRowsSurviveWithTheirCoordinates()
    {
        var (written, file) = Run("topToBottom");

        try
        {
            var path = written.Pages[0].Paths.Single();
            var rows = RowsOf(path);

            Assert.Equal(4, rows.Count);
            Assert.Equal("MoveTo", rows[0].Type);
            Assert.Equal("0", rows[0].Cells["X"]);
            Assert.Equal("4", rows[1].Cells["X"]);
            Assert.Equal("2", rows[2].Cells["Y"]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TheStyleSurvives()
    {
        var (written, file) = Run("topToBottom");

        try
        {
            var style = written.Pages[0].Paths.Single().Style;

            Assert.Equal("#FF0000", style?.Stroke, ignoreCase: true);
            Assert.Equal("#00FF00", style?.Fill, ignoreCase: true);
            Assert.Equal(0.02, style!.StrokeWidth!.Value, 3);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ThePathIsRecognisedAsAFreePath()
    {
        // It must not be read back as an ordinary node — that would relabel a custom outline.
        var (written, file) = Run("topToBottom");

        try
        {
            var page = written.Pages[0];

            Assert.Single(page.Paths);
            Assert.DoesNotContain(page.Nodes, n => n.Id == "p1");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void AnUnstatedDirectionTransposesIt()
    {
        // Not a defect: `leftToRight` swaps the axes, so the box becomes 2x4 and coordinates trade X for
        // Y. Recorded so a future reader does not mistake it for the geometry being reordered at random.
        var (written, file) = Run("leftToRight");

        try
        {
            var path = written.Pages[0].Paths.Single();

            Assert.Equal(2, path.Bounds!.W, 1);
            Assert.Equal(4, path.Bounds.H, 1);
        }
        finally { File.Delete(file); }
    }
}
