using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `Group.Order` decides the order containers stack in, whatever the strategy.
///
/// Only `FlowAndTreeLayoutEngine` read it; `SwimlaneLayoutEngine` sorted purely by `Bounds.Y`, so
/// changing an `order` from 2 to 9 moved nothing. Since `order` is a protocol field the schema
/// accepts, a value that silently changes nothing is the case the charter forbids.
///
/// The rule: a stated `order` wins; unstated falls back to geometry, which is what the swimlane engine
/// did before.
/// </summary>
public sealed class ContainerOrderTests
{
    private static Diagram Lanes(int firstOrder, int secondOrder)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 10 }
        };

        // Swimlane places lanes by Group.Members, so the membership has to be declared here rather
        // than only through Node.GroupId — a fixture that sets one and not the other tests nothing.
        page.Groups.Add(new Group
        {
            Id = "l1", Name = "Lane A", Kind = "lane", Order = firstOrder,
            Members = { "a" },
            Bounds = new Bounds(6, 7, 10, 1.6)
        });
        page.Groups.Add(new Group
        {
            Id = "l2", Name = "Lane B", Kind = "lane", Order = secondOrder,
            Members = { "b" },
            Bounds = new Bounds(6, 3, 10, 1.6)
        });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "l1", Bounds = new Bounds(4, 7, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "l2", Bounds = new Bounds(4, 3, 1.3, 0.55) });
        page.Edges.Add(new Edge { Id = "e", From = "a", To = "b" });

        return new Diagram { Id = "t", Type = "workflow", Pages = { page } };
    }

    /// <summary>
    /// Stated explicitly: the default direction is LeftToRight, and swimlane is a vertical strategy,
    /// so an unstated direction transposes the page — which turns every lane's Y into an X and makes
    /// the assertions read a constant. That is exactly how this test first failed.
    /// </summary>
    private static LayoutOptions Vertical() => new() { Direction = LayoutDirection.TopToBottom };

    private static double LaneY(Diagram d, string id) =>
        d.Pages[0].Groups.Single(g => g.Id == id).Bounds!.Y;

    [Fact]
    public void SwimlaneHonoursTheStatedOrder()
    {
        var diagram = Lanes(firstOrder: 2, secondOrder: 1);

        DiagramLayoutEngine.Apply(diagram, Vertical(), LayoutStrategy.Swimlane);

        // Higher order sits higher, matching what FlowAndTreeLayoutEngine does.
        Assert.True(LaneY(diagram, "l1") > LaneY(diagram, "l2"),
            $"l1 at {LaneY(diagram, "l1"):F2} should be above l2 at {LaneY(diagram, "l2"):F2}");
    }

    [Fact]
    public void SwappingTheOrderSwapsTheLanes()
    {
        // The property that was absent: an order change must be observable.
        var first = Lanes(firstOrder: 2, secondOrder: 1);
        DiagramLayoutEngine.Apply(first, Vertical(), LayoutStrategy.Swimlane);

        var second = Lanes(firstOrder: 1, secondOrder: 2);
        DiagramLayoutEngine.Apply(second, Vertical(), LayoutStrategy.Swimlane);

        Assert.True(LaneY(first, "l1") > LaneY(first, "l2"));
        Assert.True(LaneY(second, "l2") > LaneY(second, "l1"));
    }

    [Fact]
    public void WithoutAnOrderGeometryDecides()
    {
        // The previous behaviour must survive for diagrams that never set `order`.
        var diagram = Lanes(firstOrder: 2, secondOrder: 1);
        foreach (var g in diagram.Pages[0].Groups) g.Order = null;

        DiagramLayoutEngine.Apply(diagram, Vertical(), LayoutStrategy.Swimlane);

        // Author Y was 7 and 3, so l1 stays above.
        Assert.True(LaneY(diagram, "l1") > LaneY(diagram, "l2"));
    }

    [Fact]
    public void LanesRemainDistinct()
    {
        var diagram = Lanes(firstOrder: 2, secondOrder: 1);

        DiagramLayoutEngine.Apply(diagram, Vertical(), LayoutStrategy.Swimlane);

        var a = diagram.Pages[0].Groups.Single(g => g.Id == "l1").Bounds!;
        var b = diagram.Pages[0].Groups.Single(g => g.Id == "l2").Bounds!;

        Assert.True(Math.Abs(a.Y - b.Y) > 1.0, "lanes landed on the same row");
    }
}
