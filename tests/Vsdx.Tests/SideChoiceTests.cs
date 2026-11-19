using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The side a connector leaves from is chosen by the peer's direction alone, with no regard for what sits between
/// them — so a connector can be sent straight into a neighbour.
///
/// `Anchor` now receives every obstacle on the page but does not read them. This records the gap before it is
/// closed, and the property the fix has to satisfy: a side whose exit immediately meets another node is worse
/// than a side that does not, even if it points more directly at the peer.
/// </summary>
public sealed class SideChoiceTests
{
    private static Diagram DirectLineBlocked()
    {
        // `b` sits directly east of `a`, but `wall` is directly in that line of sight. Leaving from the right is
        // the shortest path and the worst one.
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 8 } };

        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 4, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "wall", Text = "W", Bounds = new Bounds(5, 4, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 4, 1.3, 0.55) });

        page.Edges.Add(new Edge { Id = "e", From = "a", To = "b" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void AConnectorDoesNotLeaveThroughANode()
    {
        // The end-to-end property, independent of how the side is chosen.
        var diagram = DirectLineBlocked();

        OrthogonalRouter.Route(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.through-node");
    }

    [Fact]
    public void TheBlockedSideIsTheOneThePeerWouldSuggest()
    {
        // Establishes that the case is real: the peer lies due east, so `right` is what direction alone picks.
        var diagram = DirectLineBlocked();
        var a = diagram.Pages[0].Nodes.Single(n => n.Id == "a");
        var b = diagram.Pages[0].Nodes.Single(n => n.Id == "b");

        Assert.True(b.Bounds.X > a.Bounds.X);
        Assert.Equal(a.Bounds.Y, b.Bounds.Y, 3);
    }

    [Fact]
    public void AnUnobstructedConnectorStillTakesTheDirectSide()
    {
        // The rule must not make every connector detour. With nothing in the way, the shortest side stays.
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 8 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 4, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 4, 1.3, 0.55) });
        page.Edges.Add(new Edge { Id = "e", From = "a", To = "b" });

        var diagram = new Diagram { Id = "t", Pages = { page } };

        OrthogonalRouter.Route(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);
        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.through-node");
        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.crossing");
    }
}
