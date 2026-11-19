using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Containers are always stacked vertically, so an architecture whose defining shape is parallel units comes out as
/// a list.
///
/// Measured with two containers carrying the same `order`: they render at (1.50, 3.42) and (1.50, 1.23) — one above
/// the other, both at the same x. No combination of the six strategies changes this; `Grid` explicitly falls back
/// to `Layered` when containers are present, and `Swimlane` arranges flows *inside* lanes rather than lanes in a
/// row.
///
/// The shipped `slack-cellular` case makes the cost visible: cellular architecture is three cells that must read as
/// peers, and the engine stacks them while routing connectors around the page edge.
///
/// Goal §4.2 says a professional diagram is not "几层水平色带" — parallel units are exactly the case that
/// distinction is about.
/// </summary>
public sealed class SideBySideContainersTests
{
    private static Diagram TwoContainers(int orderA, int orderB)
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };

        page.Groups.Add(new Group { Id = "a", Name = "单元 A", Kind = "layer", Order = orderA, Members = { "a1" } });
        page.Groups.Add(new Group { Id = "b", Name = "单元 B", Kind = "layer", Order = orderB, Members = { "b1" } });

        page.Nodes.Add(new Node { Id = "a1", Text = "A1", GroupId = "a" });
        page.Nodes.Add(new Node { Id = "b1", Text = "B1", GroupId = "b" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void ContainersSharingALayerSitSideBySide()
    {
        var diagram = TwoContainers(orderA: 1, orderB: 1);

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        var a = diagram.Pages[0].Groups.Single(g => g.Id == "a").Bounds!;
        var b = diagram.Pages[0].Groups.Single(g => g.Id == "b").Bounds!;

        // Same row, different columns.
        Assert.Equal(a.Y, b.Y, 2);
        Assert.NotEqual(a.X, b.X, 2);
    }

    [Fact]
    public void DifferentLayersStillStack()
    {
        // The existing behaviour must survive: `order` is what decides, and differing orders still stack.
        var diagram = TwoContainers(orderA: 1, orderB: 2);

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        var a = diagram.Pages[0].Groups.Single(g => g.Id == "a").Bounds!;
        var b = diagram.Pages[0].Groups.Single(g => g.Id == "b").Bounds!;

        Assert.NotEqual(a.Y, b.Y, 2);
    }

    [Fact]
    public void SideBySideContainersDoNotOverlap()
    {
        var diagram = TwoContainers(orderA: 1, orderB: 1);

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);
        DiagramLayout.Apply(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "group.overlap");
        Assert.DoesNotContain(report.Issues, i => i.Code == "group.member-escapes");
    }
}
