using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A lane declared through the protocol must still be a lane when it reaches layout.
///
/// The IR converter rewrote `kind: "lane"` to `"container"`, and both the swimlane engine and the
/// auto-strategy check test for `Kind == "lane"`. So a workflow declaring two lanes silently fell
/// back to layered placement — while `appliedDesignNotes` still said `layout=Swimlane`, which is
/// positive evidence of something that never happened.
///
/// Measure of the defect: swimlane and layered produced identical node coordinates, point for
/// point, on a diagram that had explicitly asked for swimlane.
/// </summary>
public sealed class SwimlaneProtocolTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string TwoLanesIr = """
    {"irVersion":"1.0","id":"l","kind":"workflow",
     "containers":[{"id":"l1","label":"Sales","kind":"lane"},
                   {"id":"l2","label":"Ops","kind":"lane"}],
     "nodes":[{"id":"a","label":"A","containerId":"l1"},
              {"id":"b","label":"B","containerId":"l2"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    private static (ExecutionResult Result, Diagram Diagram, string Path) Run(string specJson, string irJson = TwoLanesIr)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-lane-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, path);
        return (result, new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void ALaneSurvivesIrConversion()
    {
        // Checked at the converter rather than through layout, so a failure points at the rewrite
        // instead of at placement.
        var ir = JsonSerializer.Deserialize<DiagramIr>(TwoLanesIr, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);

        Assert.All(diagram.Pages[0].Groups, g => Assert.Equal("lane", g.Kind));
    }

    [Fact]
    public void ADeclaredLaneMakesAutoPickSwimlane()
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(TwoLanesIr, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);

        Assert.Equal(LayoutStrategy.Swimlane, DiagramLayoutEngine.SuggestStrategy(diagram));
    }

    [Fact]
    public void TheSwimlaneEngineActuallyHandlesTheLanes()
    {
        // Geometry equality is not the defect. With one node per lane both strategies place two
        // rows and legitimately agree; what matters is whether the swimlane engine ran at all, or
        // whether it fell through to its no-lanes fallback. The engine reports the lane count in
        // its notes, so that is the observable difference.
        var ir = JsonSerializer.Deserialize<DiagramIr>(TwoLanesIr, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);

        var swimlane = new SwimlaneLayoutEngine().Layout(diagram.Pages[0], new LayoutOptions());

        Assert.Contains(swimlane.Notes, n => n.Contains("泳道", StringComparison.Ordinal));
    }

    [Fact]
    public void LanesWithoutTheKindWouldFallBack()
    {
        // Control: strip the kind and the same engine quietly reverts to banded layout, which is
        // exactly what used to happen to every protocol-supplied lane.
        var ir = JsonSerializer.Deserialize<DiagramIr>(TwoLanesIr, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);
        foreach (var group in diagram.Pages[0].Groups) group.Kind = "container";

        var swimlane = new SwimlaneLayoutEngine().Layout(diagram.Pages[0], new LayoutOptions());

        Assert.DoesNotContain(swimlane.Notes, n => n.Contains("泳道", StringComparison.Ordinal));
    }

    [Fact]
    public void SwimlaneReportsItselfApplied()
    {
        var (result, _, path) = Run("""{"specVersion":"1.0","layout":"swimlane"}""");
        try
        {
            Assert.Contains(result.AppliedDesignNotes,
                n => n.Contains("Swimlane", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AContainerStaysAContainer()
    {
        // The fix must preserve lane while leaving ordinary containers alone.
        const string mixedIr = """
        {"irVersion":"1.0","id":"m","kind":"architecture",
         "containers":[{"id":"band","label":"Layer","kind":"layer"},
                       {"id":"lane1","label":"Swim","kind":"lane"}],
         "nodes":[{"id":"a","label":"A","containerId":"band"},
                  {"id":"b","label":"B","containerId":"lane1"}],
         "edges":[]}
        """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(mixedIr, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);
        var kinds = diagram.Pages[0].Groups.ToDictionary(g => g.Id, g => g.Kind, StringComparer.Ordinal);

        Assert.Equal("lane", kinds["lane1"]);
        Assert.NotEqual("lane", kinds["band"]);
    }

    [Fact]
    public void AnUnspecifiedKindStillDefaultsToContainer()
    {
        const string plainIr = """
        {"irVersion":"1.0","id":"p","kind":"architecture",
         "containers":[{"id":"g","label":"G"}],
         "nodes":[{"id":"a","label":"A","containerId":"g"}],"edges":[]}
        """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(plainIr, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);

        Assert.Equal("container", diagram.Pages[0].Groups[0].Kind);
    }
}
