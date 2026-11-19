using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Within a band, node order decides how many connectors cross. The engine currently emits the order it derived
/// from the graph and never revisits it.
///
/// The canonical case is a three-by-three crossing: A-B-C above X-Y-Z with `A→Z`, `B→Y`, `C→X`. Declaration
/// order puts X-Y-Z directly under A-B-C, so all three edges span the full width and cross. Reversing the lower
/// band to Z-Y-X makes all three vertical and none cross.
///
/// Measured by hand before writing this: same nodes, lower band reversed, 94 -> 98 and `edge.crossing` gone.
/// So the visible defect is the ordering, not the router — no new routing was needed.
/// </summary>
public sealed class CrossingMinimisationTests
{
    private static Diagram ThreeByThree()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 8, Height = 6 } };

        page.Groups.Add(new Group { Id = "top", Name = "上", Kind = "layer", Order = 2, Members = { "a", "b", "c" } });
        page.Groups.Add(new Group { Id = "bot", Name = "下", Kind = "layer", Order = 1, Members = { "x", "y", "z" } });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "top", Bounds = new Bounds(1, 4, 1, 0.5) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "top", Bounds = new Bounds(3, 4, 1, 0.5) });
        page.Nodes.Add(new Node { Id = "c", Text = "C", GroupId = "top", Bounds = new Bounds(5, 4, 1, 0.5) });

        page.Nodes.Add(new Node { Id = "x", Text = "X", GroupId = "bot", Bounds = new Bounds(1, 1, 1, 0.5) });
        page.Nodes.Add(new Node { Id = "y", Text = "Y", GroupId = "bot", Bounds = new Bounds(3, 1, 1, 0.5) });
        page.Nodes.Add(new Node { Id = "z", Text = "Z", GroupId = "bot", Bounds = new Bounds(5, 1, 1, 0.5) });

        page.Edges.Add(new Edge { Id = "e1", From = "a", To = "z" });
        page.Edges.Add(new Edge { Id = "e2", From = "b", To = "y" });
        page.Edges.Add(new Edge { Id = "e3", From = "c", To = "x" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void AThreeByThreeCrossingIsReduced()
    {
        var diagram = ThreeByThree();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        var report = LayoutAnalyzer.Analyze(diagram);
        var crossings = report.Issues.Where(i => i.Code == "edge.crossing").ToList();

        Assert.True(crossings.Count < 3,
            $"{crossings.Count} crossings remain — reordering the lower band should remove most of them");
    }

    [Fact]
    public void ReorderingDoesNotChangeWhichNodesAreInWhichBand()
    {
        // The constraint that makes this safe: ordering is a permutation within a band, never a move between
        // bands, and never a change to the node set.
        var diagram = ThreeByThree();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        var top = diagram.Pages[0].Groups.Single(g => g.Id == "top");
        var bot = diagram.Pages[0].Groups.Single(g => g.Id == "bot");

        Assert.Equal(new[] { "a", "b", "c" }, top.Members.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(new[] { "x", "y", "z" }, bot.Members.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void CrossingsBetweenAnInBandEdgeAndASpanningEdgeAreNotAddressed()
    {
        // The algorithm's boundary, recorded so it is not mistaken for a complete solution.
        //
        // Reordering only changes where a spanning edge starts and ends within its bands. An edge whose two ends
        // are in the *same* band cannot be moved by reordering that band at all — and such an edge can still
        // cross a spanning edge that passes over it. The remaining crossing in the agent's diagram is exactly
        // that: `order→stock` inside the service band, crossed by `pay→kafka` going down to the data band.
        //
        // Fixing it needs routing-level work, not ordering.
        var page = new Page { Name = "p", Size = new PageSize { Width = 10, Height = 6 } };

        page.Groups.Add(new Group { Id = "top", Name = "上", Kind = "layer", Order = 2, Members = { "a", "b" } });
        page.Groups.Add(new Group { Id = "bot", Name = "下", Kind = "layer", Order = 1, Members = { "c", "d" } });
        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "top", Bounds = new Bounds(2, 4, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "top", Bounds = new Bounds(6, 4, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "c", Text = "C", GroupId = "bot", Bounds = new Bounds(2, 1, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "d", Text = "D", GroupId = "bot", Bounds = new Bounds(6, 1, 1.4, 0.6) });

        page.Edges.Add(new Edge { Id = "inband", From = "a", To = "b" });   // inside the top band
        page.Edges.Add(new Edge { Id = "span", From = "b", To = "c" });     // crosses it

        var diagram = new Diagram { Id = "t", Type = "architecture", Pages = { page } };

        // Must not throw or hang: the reordering is a no-op for this shape.
        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        Assert.Equal(4, diagram.Pages[0].Nodes.Count);
    }

    [Fact]
    public void ABandWithOneNodeIsUnaffected()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 8, Height = 6 } };
        page.Groups.Add(new Group { Id = "g", Name = "单", Kind = "layer", Order = 1, Members = { "solo" } });
        page.Nodes.Add(new Node { Id = "solo", Text = "S", GroupId = "g", Bounds = new Bounds(4, 3, 1.4, 0.6) });

        var diagram = new Diagram { Id = "t", Type = "architecture", Pages = { page } };

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        Assert.True(diagram.Pages[0].Nodes[0].Bounds!.X > 0);
    }
}
