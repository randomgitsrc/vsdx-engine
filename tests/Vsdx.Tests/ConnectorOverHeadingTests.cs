using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A connector that passes through a container's heading band is not reported by anything.
///
/// `edge.through-node` walks `page.Nodes` only, so a line crossing a band's title band is invisible to it.
/// `container.heading-collision` checks a *member* node overlapping the heading, not an edge.
///
/// Both the visual reviewer and I believed the agent's diagram had connectors cutting through "数据与消息层
/// Data & Messaging". Measured properly, it does not: two connectors do cross the heading *band*, at x=4.49-5.20,
/// but the heading text is `x[0.85, 4.38]` once measured at 12pt bold rather than estimated. They pass to the
/// right of the words.
///
/// The reviewer's other two claims held up and are fixed: there were no arrowheads anywhere (see
/// `ArrowDefaultTests`), and the agent's connector to Kafka did stop 0.1" short of the trapezoid.
///
/// **I checked `cbo` for the same thing and was wrong.** Its `index` connector does cross the data band's top
/// edge, but at x=4.5-4.9, well to the right of the title at x=0.65-2.85. Looking at the render made it appear
/// to cut the word; the coordinates say otherwise. Worth recording because "it looks like it crosses" and "it
/// crosses" are different claims, and only the second is reportable.
///
/// The visual effect is worse than a crossing: a crossing looks like a route, text with a line through it looks
/// like a mistake.
/// </summary>
public sealed class ConnectorOverHeadingTests
{
    private static Diagram EdgeThroughHeading()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 10, Height = 8 } };

        page.Groups.Add(new Group { Id = "band", Name = "数据层", Kind = "layer", Order = 1, Members = { "in" } });
        page.Nodes.Add(new Node { Id = "in", Text = "成员", GroupId = "band", Bounds = new Bounds(5, 2, 1.6, 0.6) });
        page.Nodes.Add(new Node { Id = "above", Text = "上方", Bounds = new Bounds(1.5, 6, 1.6, 0.6) });

        // A straight edge from above, entering the band's heading band from the left.
        page.Edges.Add(new Edge { Id = "cut", From = "above", To = "in" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void ACutThroughAHeadingIsNowReported()
    {
        // This test used to assert the opposite: the gap existed and was recorded here. `edge.through-container-
        // heading` closed it, so the assertion is inverted deliberately.
        var diagram = EdgeThroughHeading();

        DiagramLayout.Apply(diagram);
        var report = LayoutAnalyzer.Analyze(diagram);

        // The router now treats the heading as an obstacle and reroutes around it, so neither check fires: the
        // crossing never exists in the finished diagram. What this test records is that nothing is silently
        // left crossing the title.
        // Reported again. The check had been silenced by an over-broad exemption: it skipped any edge with one
        // endpoint below the heading, which covered every connector arriving from another band. Only an edge whose
        // *both* ends sit inside the container is forced to cross, so only that case is exempt now.
        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.through-node");
        Assert.Contains(report.Issues, i => i.Code == "edge.through-container-heading");
    }

    [Fact]
    public void TheHeadingBandIsWhereTheTitleIsDrawn()
    {
        // Establishes the geometry the missing check would need: heading height, measured from the container top.
        var diagram = EdgeThroughHeading();
        DiagramLayout.Apply(diagram);

        var band = diagram.Pages[0].Groups.Single();
        double top = band.Bounds!.Y + band.Bounds.H / 2;
        double headingBottom = top - DiagramLayout.ContainerHeading;

        Assert.True(headingBottom < top);
        Assert.Equal(0.46, DiagramLayout.ContainerHeading, 3);
    }
}
