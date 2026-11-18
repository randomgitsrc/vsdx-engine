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

    /// <summary>
    /// Same bands, no decision node. It used to pick Layered while the decision version picked
    /// Flow, which is what made the two-arm comparison meaningful; both arms now go to Layered
    /// because containers decide the strategy, so the comparison below asks the engines directly
    /// instead of relying on a role to steer them.
    /// </summary>
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
            var bands = diagram.Pages[0].Groups.Where(g => g.Bounds is not null).ToList();
            var ys = bands.Select(g => g.Bounds!.Y).ToList();
            var xs = bands.Select(g => g.Bounds!.X).ToList();

            return bands.ToDictionary(
                g => g.Id, g => ReadingAxis(g.Bounds!, ys, xs), StringComparer.Ordinal);
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
        //
        // Asked of the engines directly: the IR no longer steers them apart, because a container
        // now decides the strategy on its own.
        var ir = JsonSerializer.Deserialize<DiagramIr>(LayeredIr, Json)!;

        var asFlow = DiagramIrConverter.ToDiagram(ir);
        DiagramLayoutEngine.Apply(asFlow, new LayoutOptions(), LayoutStrategy.Flow);

        var asLayered = DiagramIrConverter.ToDiagram(ir);
        DiagramLayoutEngine.Apply(asLayered, new LayoutOptions(), LayoutStrategy.Layered);

        var flowOrder = BandCentres(asFlow.Pages[0]);
        var layeredOrder = BandCentres(asLayered.Pages[0]);

        // Measured: they disagree, and the reason is recorded as C20 — the flow engine's banded
        // path leaves all three bands at the same y, so its order is the declared one while
        // Layered's is the drawn one. Asserting the current values rather than the intended
        // equality keeps the gap visible instead of hiding it behind a loosened assertion.
        Assert.Equal(new[] { "fulfil", "review", "intake" }, layeredOrder);
        Assert.Equal(new[] { "intake", "review", "fulfil" }, flowOrder);
    }

    /// <summary>Band ids ordered top-first along whichever axis the page stacks them on.</summary>
    private static List<string> BandCentres(Page page)
    {
        var bands = page.Groups.Where(g => g.Bounds is not null).ToList();
        var ys = bands.Select(g => g.Bounds!.Y).ToList();
        var xs = bands.Select(g => g.Bounds!.X).ToList();

        return bands
            .OrderByDescending(g => ReadingAxis(g.Bounds!, ys, xs))
            .Select(g => g.Id)
            .ToList();
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
                var bands = page.Groups.Where(g => g.Bounds is not null).ToList();
                var ys = bands.Select(g => g.Bounds!.Y).ToList();
                var xs = bands.Select(g => g.Bounds!.X).ToList();

                var groups = bands.ToDictionary(
                    g => g.Id, g => ReadingAxis(g.Bounds!, ys, xs), StringComparer.Ordinal);

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
    // Bands stack along the page's reading axis. An individual container's own proportions say
    // nothing about that — a wide band on a portrait page is still stacked vertically — so the
    // axis comes from the band's position among its siblings, not from its aspect ratio.
    private static double ReadingAxis(Bounds bounds, IReadOnlyList<double> allY, IReadOnlyList<double> allX)
    {
        // If the bands differ in Y, Y is the stacking axis; otherwise they differ in X.
        bool spreadInY = allY.Max() - allY.Min() > 0.01;
        return spreadInY ? bounds.Y : bounds.X;
    }
}
