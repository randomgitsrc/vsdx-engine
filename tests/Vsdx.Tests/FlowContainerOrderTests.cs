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
                .ToDictionary(g => g.Id, g => ReadingAxis(g.Bounds!), StringComparer.Ordinal);
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

        // The reading axis must run monotonically through the declared order. Its sign depends on
        // the page orientation, which the reflection now flips, so the test asserts the order rather
        // than the arithmetic direction of the numbers.
        bool ascending = y["intake"] < y["review"] && y["review"] < y["fulfil"];
        bool descending = y["intake"] > y["review"] && y["review"] > y["fulfil"];

        Assert.True(ascending || descending,
            $"reading order broke: intake={y["intake"]:F2} review={y["review"]:F2} fulfil={y["fulfil"]:F2}");
    }

    [Fact]
    public void LayeredEngineKeepsDeclaredReadingOrder()
    {
        // The control: this path was already correct, and must stay that way.
        var y = BandHeights(LayeredIr, "");

        // Monotonic along the reading axis, in whichever direction that axis runs. The sheet is
        // reflected so the flow travels left to right, which makes the coordinates increase rather
        // than decrease; asserting the direction of the numbers would tie the test to the
        // reflection rather than to the order it is meant to protect.
        Assert.True(
            (y["intake"] > y["review"] && y["review"] > y["fulfil"]) ||
            (y["intake"] < y["review"] && y["review"] < y["fulfil"]),
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
                    .ToDictionary(g => g.Id, g => ReadingAxis(g.Bounds!), StringComparer.Ordinal);

                var first = groups.Keys.Single(k => k.StartsWith("a"));
                var second = groups.Keys.Single(k => k.StartsWith("b"));

                // The two bands must stay distinct along the reading axis; which side is larger
                // depends on the page orientation, not on whether the order was kept.
                Assert.True(Math.Abs(groups[first] - groups[second]) > 0.1,
                    $"{page.Name}: {first} ({groups[first]:F2}) and {second} ({groups[second]:F2}) " +
                    "landed on the same line");
            }
        }
        finally
        {
            File.Delete(output);
        }
    }

    /// <summary>
    /// The coordinate bands are ordered along.
    ///
    /// Bands stack vertically in the engine's own arrangement, so Y. Highlighting `leftToRight`
    /// transposes the page, after which the same ordering runs along X. Comparing Y in both cases
    /// made the tests read a constant.
    /// </summary>
    private static double ReadingAxis(Bounds bounds) => bounds.H > bounds.W ? bounds.Y : bounds.X;
}
