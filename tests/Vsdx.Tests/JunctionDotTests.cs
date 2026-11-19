using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Where a bus splits, nothing marks the split — the branches simply part and the reader has to infer that they
/// come from the same place. Every professional architecture drawing puts a dot there.
///
/// Measured on `deploy-topology`: the three branches from `lb` all run along y=4.85 and turn down at their own
/// target's x. There are three turn points and no mark at any of them.
///
/// The value is not decoration. Without a dot, a line that happens to end near another line is indistinguishable
/// from a line that joins it — the fourth review's finding that two API instances appear to be linked to each other
/// is the same failure at the node end. A junction mark says "these are one wire" and, by its absence, says the
/// opposite.
/// </summary>
public sealed class JunctionDotTests
{
    private static Diagram OneBusThreeBranches()
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
    public void ABusMarksWhereItSplits()
    {
        var diagram = OneBusThreeBranches();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);
        OrthogonalRouter.Route(diagram);
        JunctionMarks.Apply(diagram);

        var page = diagram.Pages[0];

        // A mark is a small node with no text, placed on the bus.
        var dots = page.Nodes.Where(n => n.Shape == "Ellipse" && string.IsNullOrEmpty(n.Text)).ToList();

        Assert.True(dots.Count >= 2,
            $"a three-branch bus produced {dots.Count} junction marks; every branch needs one where it leaves the trunk");
    }

    [Fact]
    public void AMarkSitsOnTheBranchItTerminates()
    {
        var diagram = OneBusThreeBranches();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);
        OrthogonalRouter.Route(diagram);
        JunctionMarks.Apply(diagram);

        var page = diagram.Pages[0];
        var dots = page.Nodes.Where(n => n.Shape == "Ellipse" && string.IsNullOrEmpty(n.Text)).ToList();

        foreach (var dot in dots)
        {
            // Each mark must land on some edge's own route, not float between them.
            bool onARoute = page.Edges
                .Where(e => e.Waypoints is { Count: > 0 })
                .Any(e => e.Waypoints!.Any(w =>
                    Math.Abs(w.X - dot.Bounds.X) < 0.05 && Math.Abs(w.Y - dot.Bounds.Y) < 0.05));

            Assert.True(onARoute, $"junction mark at ({dot.Bounds.X:F2},{dot.Bounds.Y:F2}) is not on any route");
        }
    }

    [Fact]
    public void MarksDoNotChangeTheDrawingBeyondAddingThemselves()
    {
        // The dots are annotations. If adding them shifts a node, they are not annotations any more.
        var plain = OneBusThreeBranches();
        var marked = OneBusThreeBranches();

        DiagramLayoutEngine.Apply(plain,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);
        DiagramLayoutEngine.Apply(marked,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);
        OrthogonalRouter.Route(marked);
        JunctionMarks.Apply(marked);

        var before = plain.Pages[0].Nodes.OrderBy(n => n.Id).Select(n => (n.Id, n.Bounds.X, n.Bounds.Y)).ToList();
        var after = marked.Pages[0].Nodes
            .Where(n => n.Shape != "Ellipse" || !string.IsNullOrEmpty(n.Text))
            .OrderBy(n => n.Id).Select(n => (n.Id, n.Bounds.X, n.Bounds.Y)).ToList();

        Assert.Equal(before, after);
    }
}
