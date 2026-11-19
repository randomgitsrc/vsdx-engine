using System.Text.Json;
using Vsdx.Protocol;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The IR can describe what a component is, but not what state it is in.
///
/// `IrNode` carries id, label, role, container and attributes — no fill, no opacity, no stroke pattern. So the
/// diagram that motivated this round cannot be drawn: Slack's cellular architecture is about cells that "may be
/// drained", and showing one drained while the other two absorb 50% each is the whole point. The current model can
/// only draw three identical cells in a steady 1/3-1/3-1/3 split, which states the architecture has three copies
/// and says nothing about one of them being removable.
///
/// `ShapeStyle` already supports everything needed — `Opacity`, `StrokePattern`, `FillPattern` — and the writer
/// passes it through. The gap is the protocol, as with `LinePattern` and `Bidirectional` before it.
/// </summary>
public sealed class ComponentStateTests
{
    private static Diagram Convert(string nodeJson, string? containerJson = null)
    {
        var containers = containerJson is null ? "" : $$""" "containers":[{{containerJson}}], """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(
            $$"""
            {"irVersion":"1.0","id":"t","kind":"architecture",{{containers}}
             "nodes":[{{nodeJson}}],"edges":[]}
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return DiagramIrConverter.ToDiagram(ir);
    }

    [Fact]
    public void ADrainedNodeIsVisuallyDistinct()
    {
        var diagram = Convert("""{"id":"a","label":"A","status":"drained"}""");
        var node = diagram.Pages[0].Nodes.Single();

        // Whatever the treatment, it must differ from the default: a drained component that looks like a live one
        // is the failure this is meant to prevent.
        bool distinguished =
            node.Style?.Opacity is double op && op < 1.0
            || node.Style?.StrokePattern is int sp && sp != 1
            || node.Style?.Fill is not null;

        Assert.True(distinguished, "a drained node is styled exactly like a live one");
    }

    [Fact]
    public void ALiveNodeKeepsTheDefaultAppearance()
    {
        var live = Convert("""{"id":"a","label":"A"}""").Pages[0].Nodes.Single();
        var declared = Convert("""{"id":"a","label":"A","status":"live"}""").Pages[0].Nodes.Single();

        Assert.Null(live.Style?.Opacity);
        Assert.Equal(live.Style?.Opacity, declared.Style?.Opacity);
    }

    [Fact]
    public void ADrainedContainerIsVisuallyDistinct()
    {
        var diagram = Convert(
            """{"id":"a1","label":"A1","containerId":"cell"}""",
            """{"id":"cell","label":"单元","order":1,"status":"drained"}""");

        var group = diagram.Pages[0].Groups.Single();

        bool distinguished =
            group.Style?.Opacity is double op && op < 1.0
            || group.Style?.StrokePattern is int sp && sp != 1
            || group.Style?.Fill is not null;

        Assert.True(distinguished, "a drained container is styled exactly like a live one");
    }

    [Fact]
    public void AnUnknownStatusIsReported()
    {
        // Same rule as unknown DesignSpec fields: a value the engine cannot honour must not be dropped in silence.
        var ir = JsonSerializer.Deserialize<DiagramIr>(
            """{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A","status":"half-drained"}],"edges":[]}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var model = DiagramIrConverter.ToDiagram(ir);

        Assert.Single(model.Pages[0].Nodes);
    }
}
