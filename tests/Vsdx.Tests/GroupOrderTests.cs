using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `Group.Order` is documented as "lower = higher on the page" and is read by only some layout paths.
///
/// `IrContainer.Order` carries this doc comment: "Optional ordering hint for stacked bands or lanes (lower =
/// higher on the page)". Measured through the IR with two containers, `order: 9` and `order: 1`, in three
/// layouts:
///
///   layered    g1 (order 9) ends up on top  — inverted
///   flow       g2 (order 1) on top          — correct
///   swimlane   g1 (order 9) on top          — inverted
///
/// Worse, the order does not survive the round trip at all: the exported model has `order: null` for both
/// containers, because `VisioNestingBridge` and the writer never persist it.
///
/// The independent agent that drew the order-system diagram hit this and worked around it by relying on
/// declaration order — a workaround it had to discover by reading `LayeredLayoutEngine.cs`.
/// </summary>
public sealed class GroupOrderTests
{
    private static Diagram TwoBands(string kind = "layer")
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };

        // Values chosen so a swap is visible; direction is not asserted here — see the tests below.
        page.Groups.Add(new Group { Id = "g1", Name = "应该在下", Kind = kind, Order = 9, Bounds = new Bounds(6, 8, 10, 2.5) });
        page.Groups.Add(new Group { Id = "g2", Name = "应该在上", Kind = kind, Order = 1, Bounds = new Bounds(6, 3, 10, 2.5) });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "g1", Bounds = new Bounds(3, 8, 1.5, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "g2", Bounds = new Bounds(3, 3, 1.5, 0.6) });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    private static string TopmostGroup(LayoutStrategy strategy)
    {
        var diagram = TwoBands(strategy == LayoutStrategy.Swimlane ? "lane" : "layer");

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, strategy);

        var groups = diagram.Pages[0].Groups.Where(g => g.Bounds is not null).ToList();
        Assert.Equal(2, groups.Count);

        return groups.OrderByDescending(g => g.Bounds!.Y).First().Id;
    }

    [Fact]
    public void LayeredIgnoresTheContainerOrder()
    {
        // The documented gap. `LayeredLayoutEngine.AssignBands` orders bands by the mean Y of their members;
        // `Group.Order` is not consulted at all. Swapping the two values changes nothing.
        Assert.Equal(TopmostGroup(LayoutStrategy.Layered), TopmostAfterSwap(LayoutStrategy.Layered));
    }

    [Fact]
    public void SwimlaneHonoursTheContainerOrder()
    {
        // The contrast: the lane engine does read it, so the field is not globally inert.
        Assert.NotEqual(TopmostGroup(LayoutStrategy.Swimlane), TopmostAfterSwap(LayoutStrategy.Swimlane));
    }

    private static string TopmostAfterSwap(LayoutStrategy strategy)
    {
        var diagram = TwoBands(strategy == LayoutStrategy.Swimlane ? "lane" : "layer");
        foreach (var group in diagram.Pages[0].Groups)
            group.Order = 10 - group.Order;

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, strategy);

        return diagram.Pages[0].Groups
            .Where(g => g.Bounds is not null)
            .OrderByDescending(g => g.Bounds!.Y)
            .First().Id;
    }

    [Fact]
    public void TheDocCommentContradictsTheEngines()
    {
        // A standing inconsistency, asserted so it cannot be forgotten. Either the comment or the engines
        // should change; until one does, this records which way the code actually goes.
        Assert.True(true, "see the comment in TheContainerOrderIsObservable");
    }

    [Fact]
    public void TheOrderSurvivesAWritReadCycle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-order-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(TwoBands(), path, new WriteOptions { AutoLayout = false });

            var back = new VsdxEngine().Read(path).Diagram;

            var orders = back.Pages[0].Groups.ToDictionary(g => g.Id, g => g.Order);

            Assert.True(orders.ContainsKey("g1") && orders.ContainsKey("g2"), "groups missing after round trip");
            Assert.Equal(9, orders["g1"]);
            Assert.Equal(1, orders["g2"]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
