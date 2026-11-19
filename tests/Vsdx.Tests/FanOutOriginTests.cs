using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A bus fan-out sends every branch from the same point on the source, so three connectors leave as one line and
/// only separate at the bus.
///
/// Measured on `slack-cellular`: the three edges out of `Envoy` all begin at (7.05, 2.65) — the same waypoint, to
/// two decimals. The render shows a knot at the hexagon's bottom vertex.
///
/// **The cause is the bus itself**, which I added two rounds ago. `SpreadOffset` has always existed to fan connectors
/// out along the side they leave from, but `RouteFanOuts` writes waypoints directly and never calls `Anchor`, so the
/// spread never runs for the one case that needs it most.
/// </summary>
public sealed class FanOutOriginTests
{
    private static Diagram OneSourceThreeTargets()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 16, Height = 9 } };

        page.Groups.Add(new Group { Id = "a", Name = "A", Kind = "layer", Order = 1, Members = { "a1" } });
        page.Groups.Add(new Group { Id = "b", Name = "B", Kind = "layer", Order = 1, Members = { "b1" } });
        page.Groups.Add(new Group { Id = "c", Name = "C", Kind = "layer", Order = 1, Members = { "c1" } });

        page.Nodes.Add(new Node { Id = "src", Text = "网关" });
        page.Nodes.Add(new Node { Id = "a1", Text = "A1", GroupId = "a" });
        page.Nodes.Add(new Node { Id = "b1", Text = "B1", GroupId = "b" });
        page.Nodes.Add(new Node { Id = "c1", Text = "C1", GroupId = "c" });

        page.Edges.Add(new Edge { Id = "e1", From = "src", To = "a1", Label = "33%" });
        page.Edges.Add(new Edge { Id = "e2", From = "src", To = "b1", Label = "33%" });
        page.Edges.Add(new Edge { Id = "e3", From = "src", To = "c1", Label = "33%" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void BranchesOfOneBusLeaveTheSourceAtDifferentPoints()
    {
        var diagram = OneSourceThreeTargets();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);
        OrthogonalRouter.Route(diagram);

        var starts = diagram.Pages[0].Edges
            .Where(e => e.Waypoints is { Count: > 0 })
            .Select(e => e.Waypoints![0])
            .ToList();

        Assert.True(starts.Count >= 2, "expected at least two routed branches");

        // All three leaving from one pixel draws a knot where they overlap.
        int distinct = starts
            .Select(p => (Math.Round(p.X, 2), Math.Round(p.Y, 2)))
            .Distinct()
            .Count();

        Assert.True(distinct > 1, $"all {starts.Count} branches leave from the same point");
    }
}
