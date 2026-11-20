using Vsdx.Engine;
using System.Text.Json;
using Vsdx.Protocol;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A component's lifecycle state survives into the picture but not into the model, so reading the file back loses it.
///
/// Measured: an IR node with `"status": "drained"` renders with a dashed border and a faded fill — the visual side
/// works — but `export-model` returns no status at all, and `Node` has no field to carry one. Everything downstream
/// that reads a model therefore cannot tell a drained component from a live one.
///
/// This is the pattern the project forbids by name: accepted by the protocol, permitted by the schema, applied at
/// execution, and unrecoverable afterwards.
/// </summary>
public sealed class StatusRoundTripTests
{
    [Fact]
    public void ADrainedNodeKeepsItsStateWhenReadBack()
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(
            """
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "nodes":[
               {"id":"a","label":"A","status":"drained"},
               {"id":"b","label":"B"}],
             "edges":[]}
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-status-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(DiagramIrConverter.ToDiagram(ir), output, new WriteOptions { AutoLayout = true });

            var back = new VsdxEngine().Read(output).Diagram.Pages[0];
            var drained = back.Nodes.Single(n => n.Id == "a");
            var live = back.Nodes.Single(n => n.Id == "b");

            Assert.Equal("drained", drained.Status);
            Assert.True(live.Status is null or "live", $"a plain node came back as '{live.Status}'");
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public void ADrainedContainerKeepsItsStateWhenReadBack()
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(
            """
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "containers":[{"id":"c","label":"C","order":1,"status":"drained"}],
             "nodes":[{"id":"a","label":"A","containerId":"c"}],
             "edges":[]}
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-status2-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(DiagramIrConverter.ToDiagram(ir), output, new WriteOptions { AutoLayout = true });

            var back = new VsdxEngine().Read(output).Diagram.Pages[0];

            Assert.Equal("drained", back.Groups.Single().Status);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }
}
