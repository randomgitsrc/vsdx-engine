using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The side a connector leaves from is chosen by the peer's direction, and a side that points at the peer can
/// still be the wrong one.
///
/// Measured on `deploy-topology`'s `d4` (lb → api2). The load balancer sits beside the WAF, and the peer is to the
/// south-west. Testing every side's line against the WAF, with the router's own 0.16" clearance:
///
///   left    anchor (6.39, 5.53)   crosses
///   right   anchor (8.11, 5.53)   clear
///   top     anchor (7.25, 5.89)   crosses
///   bottom  anchor (7.25, 5.16)   clear
///
/// Direction alone picks `left` — the worst of the four. Twice I wrote that choice into the model and twice the
/// diagram failed on `edge.through-node`, because I replaced a side OfficeIMO had picked correctly with one my
/// arithmetic preferred.
/// </summary>
public sealed class SideAvoidsNodesTests
{
    /// <summary>The load balancer, the WAF beside it, and a service two bands down.</summary>
    private static Diagram LoadBalancerBesideWaf()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };

        page.Nodes.Add(new Node { Id = "lb", Text = "负载均衡", Bounds = new Bounds(7.25, 5.525, 1.725, 0.725) });
        page.Nodes.Add(new Node { Id = "waf", Text = "WAF", Bounds = new Bounds(5.0, 5.525, 1.725, 0.725) });
        page.Nodes.Add(new Node { Id = "api2", Text = "API 2", Bounds = new Bounds(3.35, 3.25, 1.3, 0.55) });

        page.Edges.Add(new Edge { Id = "d4", From = "lb", To = "api2" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void TheConnectorDoesNotCutThroughTheWaf()
    {
        var diagram = LoadBalancerBesideWaf();

        OrthogonalRouter.Route(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.through-node");
    }

    [Fact]
    public void AChosenSideNeverPointsStraightAtANeighbour()
    {
        // The property behind the failure: whichever side is used, its first stretch must be free. Asserted on the
        // routed result rather than on the choice, so the rule can be implemented anywhere.
        var diagram = LoadBalancerBesideWaf();

        OrthogonalRouter.Route(diagram);

        var edge = diagram.Pages[0].Edges.Single();

        // Straight or waypointed, the drawn path must not enter the WAF's padded box.
        Assert.Empty(edge.Waypoints ?? new List<Point>());
        Assert.DoesNotContain("edge.through-node", LayoutAnalyzer.Analyze(diagram).Issues.Select(i => i.Code));
    }

    [Fact]
    public void AnUnobstructedDiagramIsUnaffected()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 8 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 4, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 4, 1.3, 0.55) });
        page.Edges.Add(new Edge { Id = "e", From = "a", To = "b" });

        var diagram = new Diagram { Id = "t", Pages = { page } };

        OrthogonalRouter.Route(diagram);

        Assert.DoesNotContain("edge.through-node",
            LayoutAnalyzer.Analyze(diagram).Issues.Select(i => i.Code));
    }
}
