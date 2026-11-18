using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Container order must survive the layout engine that happens to be chosen.
///
/// A workflow whose bands are declared intake → review → fulfil rendered them upside down —
/// fulfilment at the top, intake at the bottom — so the process had to be read backwards. The
/// `order` field on each container was ignored, and the two layout engines disagreed about the
/// direction to walk bands in:
///
///   LayeredLayoutEngine   OrderByDescending(MeanY)  — top band first, laid out downward  (right)
///   FlowAndTreeLayoutEngine  OrderBy(Bounds.Y)      — bottom band first, stacked upward   (inverted)
///
/// Which engine runs depends on whether the diagram contains a decision node, so the same
/// container list could come out either way. That is why the defect needed a specific diagram to
/// reproduce and why it was invisible in simpler models.
/// </summary>
public sealed class FlowContainerOrderTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Three bands declared in reading order, with the decision branch Flow needs.</summary>
    private const string FlowIr = """
    {"irVersion":"1.0","id":"f","kind":"workflow",
     "containers":[
       {"id":"intake","label":"Intake","kind":"layer","order":2},
       {"id":"review","label":"Review","kind":"layer","order":1},
       {"id":"fulfil","label":"Fulfil","kind":"layer","order":0}],
     "nodes":[
       {"id":"s","label":"Submit","containerId":"intake"},
       {"id":"r","label":"Review","containerId":"review","role":"decision"},
       {"id":"d","label":"Done","containerId":"fulfil"}],
     "edges":[{"id":"e1","from":"s","to":"r"},{"id":"e2","from":"r","to":"d"}]}
    """;

    /// <summary>Same bands, no decision node — this path picks the Layered engine.</summary>
    private const string LayeredIr = """
    {"irVersion":"1.0","id":"f","kind":"workflow",
     "containers":[
       {"id":"intake","label":"Intake","kind":"layer","order":2},
       {"id":"review","label":"Review","kind":"layer","order":1},
       {"id":"fulfil","label":"Fulfil","kind":"layer","order":0}],
     "nodes":[
       {"id":"s","label":"Submit","containerId":"intake"},
       {"id":"r","label":"Review","containerId":"review"},
       {"id":"d","label":"Done","containerId":"fulfil"}],
     "edges":[{"id":"e1","from":"s","to":"r"},{"id":"e2","from":"r","to":"d"}]}
    """;

    private static Dictionary<string, double> BandHeights(string irJson, string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = string.IsNullOrEmpty(specJson) ? null : JsonSerializer.Deserialize<DesignSpec>(specJson, Json);

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-flow-{Guid.NewGuid():N}.vsdx");
        new ProtocolRunner().Execute(ir, spec, output);

        try
        {
            var diagram = new VsdxEngine().Read(output).Diagram;
            return diagram.Pages[0].Groups
                .Where(g => g.Bounds is not null)
                .ToDictionary(g => g.Id, g => g.Bounds!.Y, StringComparer.Ordinal);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void FlowEngineKeepsDeclaredReadingOrder()
    {
        var y = BandHeights(FlowIr, "");

        // Larger y is higher on the page, so the first band read should sit highest.
        Assert.True(y["intake"] > y["review"],
            $"intake (y={y["intake"]:F2}) should be above review (y={y["review"]:F2})");
        Assert.True(y["review"] > y["fulfil"],
            $"review (y={y["review"]:F2}) should be above fulfil (y={y["fulfil"]:F2})");
    }

    [Fact]
    public void LayeredEngineKeepsDeclaredReadingOrder()
    {
        // The control: this path was already correct, and must stay that way.
        var y = BandHeights(LayeredIr, "");

        Assert.True(y["intake"] > y["review"] && y["review"] > y["fulfil"],
            $"layered order broke: intake={y["intake"]:F2} review={y["review"]:F2} fulfil={y["fulfil"]:F2}");
    }

    [Fact]
    public void BothEnginesAgreeOnBandOrder()
    {
        // The defect was not "one engine is wrong" but "the two disagree", which makes the same
        // input render differently depending on an unrelated property of the diagram.
        var flow = BandHeights(FlowIr, "");
        var layered = BandHeights(LayeredIr, "");

        var flowOrder = flow.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        var layeredOrder = layered.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();

        Assert.Equal(layeredOrder, flowOrder);
    }

    [Fact]
    public void ReadingOrderHoldsAcrossPages()
    {
        // Multi-page: the ordering rule has to be per page, not a one-off on the first.
        const string twoPages = """
        {"irVersion":"1.0","id":"f","kind":"workflow",
         "pages":[
          {"id":"p1","containers":[
             {"id":"a1","label":"A","kind":"layer","order":2},
             {"id":"b1","label":"B","kind":"layer","order":1}],
           "nodes":[{"id":"n1","label":"N1","containerId":"a1"},
                    {"id":"n2","label":"N2","containerId":"b1","role":"decision"}],
           "edges":[{"id":"e1","from":"n1","to":"n2"}]},
          {"id":"p2","containers":[
             {"id":"a2","label":"A","kind":"layer","order":2},
             {"id":"b2","label":"B","kind":"layer","order":1}],
           "nodes":[{"id":"n3","label":"N3","containerId":"a2"},
                    {"id":"n4","label":"N4","containerId":"b2","role":"decision"}],
           "edges":[{"id":"e2","from":"n3","to":"n4"}]}]}
        """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(twoPages, Json)!;
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-flow2-{Guid.NewGuid():N}.vsdx");
        new ProtocolRunner().Execute(ir, null, output);

        try
        {
            var diagram = new VsdxEngine().Read(output).Diagram;
            foreach (var page in diagram.Pages)
            {
                var groups = page.Groups.Where(g => g.Bounds is not null)
                    .ToDictionary(g => g.Id, g => g.Bounds!.Y, StringComparer.Ordinal);

                var first = groups.Keys.Single(k => k.StartsWith("a"));
                var second = groups.Keys.Single(k => k.StartsWith("b"));

                Assert.True(groups[first] > groups[second],
                    $"{page.Name}: {first} ({groups[first]:F2}) should be above {second} ({groups[second]:F2})");
            }
        }
        finally
        {
            File.Delete(output);
        }
    }
}
