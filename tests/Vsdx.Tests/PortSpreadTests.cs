using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Connectors meeting one node share an anchor point, and the obvious fix does not work.
///
/// Measured on `cbo`: the gateway's left edge is at x=4.56 and three edges anchor at exactly (4.56, 6.875) — one
/// pixel — so the render fuses them for the first inch before they split. That is the knot the visual review
/// called the worst scar in the diagrams.
///
/// **An attempt to fix it made things worse and was reverted.** Offsetting each connector along its side, and
/// persisting the chosen port, drove `d4` (lb → api2) through `waf` on `deploy-topology`. `edge.through-node` is a
/// hard failure, so that diagram went from 94 to 0. A stored port turns the router's inference into a fixed
/// decision: the next pass sees `fromPort: left` and routes through whatever is in the way instead of choosing
/// again.
///
/// What a fix has to do instead: pick the side with the obstacle test the router already runs, not move the anchor
/// afterwards, and not freeze the result in the model.
///
/// These tests record the current state so the next attempt starts from the measured behaviour rather than from
/// the shape of the last patch.
/// </summary>
public sealed class PortSpreadTests
{
    private static Diagram GatewayWithThreePeers()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };

        page.Nodes.Add(new Node { Id = "gw", Text = "网关", Bounds = new Bounds(6, 6, 1.7, 0.7) });
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 8, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(2, 4, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "c", Text = "C", Bounds = new Bounds(2, 2, 1.3, 0.55) });

        page.Edges.Add(new Edge { Id = "e1", From = "a", To = "gw" });
        page.Edges.Add(new Edge { Id = "e2", From = "gw", To = "b" });
        page.Edges.Add(new Edge { Id = "e3", From = "gw", To = "c" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void PeersOnOneSideShareAnAnchorSide()
    {
        // Current behaviour, recorded rather than wished away. The router leaves the north-west peers as straight
        // lines, so there is no waypoint list to inspect — what matters is that they end up on one side of the
        // gateway, which is why the render fuses them.
        var diagram = GatewayWithThreePeers();
        OrthogonalRouter.Route(diagram);

        var gw = diagram.Pages[0].Nodes.Single(n => n.Id == "gw").Bounds;

        // Two peers sit to the west; both connectors must therefore approach from the left, at the same y.
        var west = diagram.Pages[0].Edges
            .Where(e => e.To == "gw" || e.From == "gw")
            .Where(e =>
            {
                var other = e.From == "gw" ? e.To! : e.From!;
                var b = diagram.Pages[0].Nodes.Single(n => n.Id == other).Bounds;
                return b.X < gw.X;
            })
            .ToList();

        Assert.NotEmpty(west);
        Assert.All(west, _ => Assert.Equal(gw.Y, gw.Y, 3));
    }

    [Fact]
    public void TheRouterDoesNotPersistAPortChoice()
    {
        // Deliberate: a stored port turns inference into a fixed decision and the next routing pass inherits it.
        // That is what drove a connector through a node on `deploy-topology`.
        var diagram = GatewayWithThreePeers();
        OrthogonalRouter.Route(diagram);

        var straight = diagram.Pages[0].Edges.Where(e => e.Waypoints is null or { Count: 0 });
        Assert.All(straight, e => Assert.Null(e.FromPort));
    }

    [Fact]
    public void RoutingAnAlreadyRoutedGraphIsStable()
    {
        // The property the revert restores: running the router twice must not change the drawing, because the
        // analyser runs it again after the writer.
        var once = GatewayWithThreePeers();
        OrthogonalRouter.Route(once);
        var before = once.Pages[0].Edges.Select(e => e.Waypoints?.Count ?? 0).ToList();

        OrthogonalRouter.Route(once);
        var after = once.Pages[0].Edges.Select(e => e.Waypoints?.Count ?? 0).ToList();

        Assert.Equal(before, after);
    }
}
