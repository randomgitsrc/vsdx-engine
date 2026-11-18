using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A label position set through the protocol must reach the model.
///
/// The model has `LabelPosition`, the writer uses it for `LabelPlacement`, and the analyser reads it
/// (C9). The IR did not carry it at all, so the value was dropped in conversion and the execute path
/// — the one an agent actually drives — could not move a label. Measured: setting 0.25 and 0.75 on
/// four edges left the overlap count unchanged, because all four arrived at the model at 0.5.
///
/// Adding an optional field is backward compatible: existing IR documents keep working and keep the
/// default.
/// </summary>
public sealed class IrLabelPositionTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string Ir = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
     "edges":[{"id":"e1","from":"a","to":"b","label":"calls","labelPosition":0.25},
              {"id":"e2","from":"a","to":"b","label":"rest"}]}
    """;

    [Fact]
    public void TheFieldReachesTheModel()
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(Ir, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);

        var e1 = diagram.Pages[0].Edges.Single(e => e.Id == "e1");
        Assert.Equal(0.25, e1.LabelPosition);
    }

    [Fact]
    public void AnEdgeWithoutTheFieldStaysNull()
    {
        // Null means "use the default"; filling it in would make every edge look explicitly placed.
        var ir = JsonSerializer.Deserialize<DiagramIr>(Ir, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);

        var e2 = diagram.Pages[0].Edges.Single(e => e.Id == "e2");
        Assert.Null(e2.LabelPosition);
    }

    [Fact]
    public void SettingAPositionChangesWhereTheAnalyserThinksTheLabelIs()
    {
        // The practical consequence, and the reason the field must exist: with two edges between the
        // same nodes, separating their positions is the only way to clear the overlap.
        // Anchors matter here: without them the converter places every node at the origin, the two
        // edges collapse to a point, and no position value can separate their labels.
        const string coincident = """
        {"irVersion":"1.0","id":"p","kind":"architecture",
         "nodes":[{"id":"a","label":"A","anchor":{"x":2,"y":4}},
                  {"id":"b","label":"B","anchor":{"x":10,"y":4}}],
         "edges":[{"id":"e1","from":"a","to":"b","label":"gRPC","labelPosition":0.2},
                  {"id":"e2","from":"a","to":"b","label":"REST","labelPosition":0.8}]}
        """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(coincident, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "label.overlaps-label");
    }

    [Fact]
    public void BothAtTheDefaultStillOverlap()
    {
        // Control: without positions, the two labels coincide and the check fires.
        const string noPositions = """
        {"irVersion":"1.0","id":"p","kind":"architecture",
         "nodes":[{"id":"a","label":"A","anchor":{"x":2,"y":4}},
                  {"id":"b","label":"B","anchor":{"x":10,"y":4}}],
         "edges":[{"id":"e1","from":"a","to":"b","label":"gRPC"},
                  {"id":"e2","from":"a","to":"b","label":"REST"}]}
        """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(noPositions, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);

        Assert.Contains(LayoutAnalyzer.Analyze(diagram).Issues,
            i => i.Code == "label.overlaps-label");
    }
}
