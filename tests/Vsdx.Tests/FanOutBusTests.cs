using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Three connectors from one source to three side-by-side targets are routed independently, so they take three
/// different shapes and stop reading as one decision.
///
/// Measured on `slack-cellular`: `Envoy` sits at x=7.05 while the three cells centre on 6.31. The connector to the
/// leftmost cell detours twice — out to (6.15, 4.38), across to (2.4, 4.38) — while the other two are straight
/// lines. One is a routed path, two are diagonals; nothing tells the reader they are the same fan-out.
///
/// The professional drawing for this is a bus: one horizontal run leaving the source, then a vertical drop into
/// each target. All three then look alike by construction, and none of them crosses a container on the way.
/// </summary>
public sealed class FanOutBusTests
{
    private static Diagram OneSourceThreeTargets()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 16, Height = 9 } };

        page.Groups.Add(new Group { Id = "a", Name = "A", Kind = "layer", Order = 1, Members = { "a1" } });
        page.Groups.Add(new Group { Id = "b", Name = "B", Kind = "layer", Order = 1, Members = { "b1" } });
        page.Groups.Add(new Group { Id = "c", Name = "C", Kind = "layer", Order = 1, Members = { "c1" } });

        // The source is deliberately offset to the right of the middle target, which is what forces the leftmost
        // connector to detour.
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
    public void EveryConnectorInAFanOutTakesTheSameShape()
    {
        var diagram = OneSourceThreeTargets();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);
        OrthogonalRouter.Route(diagram);

        var edges = diagram.Pages[0].Edges;

        // Either all straight or all routed — a mixture reads as unrelated links.
        int straight = edges.Count(e => e.Waypoints is null or { Count: 0 });
        int routed = edges.Count - straight;

        Assert.True(straight == 0 || routed == 0,
            $"{straight} straight and {routed} routed out of one fan-out of three");
    }

    [Fact]
    public void AFanOutDoesNotEnterAnotherTargetsContainer()
    {
        var diagram = OneSourceThreeTargets();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);
        OrthogonalRouter.Route(diagram);

        var page = diagram.Pages[0];
        var report = LayoutAnalyzer.Analyze(diagram);

        // The crossing check covers edges through nodes; a line through a *container* is what makes a fan-out look
        // like a chain.
        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.through-container-heading");
    }
}
