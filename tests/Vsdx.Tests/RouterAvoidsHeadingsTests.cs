using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The router treats nodes as obstacles and container headings as free space, so a connector can be routed
/// straight through a band's title.
///
/// `PathHitsShapes` accepts `List<Node> others` and tests each node's padded box. Containers are not in that list,
/// and their heading bands are not tested at all — so `edge.through-container-heading` reports crossings the
/// router had no way to avoid.
///
/// Measured on `cbo`: two connectors cross a heading, one routed (`e3`, whose final vertical segment drops through
/// the service band's title at x≈2.2) and one straight (`e5`, a vertical line at x=1.5). Fixing only the routed
/// case would leave half the problem.
/// </summary>
public sealed class RouterAvoidsHeadingsTests
{
    private static Diagram BandWithObstacleInTheWay()
    {
        // A band whose heading sits between two nodes, so a connector between them has to deal with it.
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 9 } };

        page.Groups.Add(new Group { Id = "band", Name = "服务层", Kind = "layer", Order = 1, Members = { "a" } });
        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "band", Bounds = new Bounds(6, 2, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(2, 6, 1.4, 0.6) });

        page.Edges.Add(new Edge { Id = "e", From = "b", To = "a" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void ARoutedConnectorDoesNotCutTheHeading()
    {
        var diagram = BandWithObstacleInTheWay();

        OrthogonalRouter.Route(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.through-container-heading");
    }

    [Fact]
    public void AStraightConnectorDoesNotCutTheHeadingEither()
    {
        // The other half: `e5` in `cbo` is straight and never enters the routing search, so the avoidance has to
        // reach the decision that lets a straight edge stay straight.
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 9 } };

        page.Groups.Add(new Group { Id = "band", Name = "数据层", Kind = "layer", Order = 1, Members = { "a" } });
        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "band", Bounds = new Bounds(2, 2, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(2, 6, 1.4, 0.6) });

        page.Edges.Add(new Edge { Id = "e", From = "b", To = "a" });

        var diagram = new Diagram { Id = "t", Type = "architecture", Pages = { page } };

        OrthogonalRouter.Route(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.through-container-heading");
    }

    [Fact]
    public void TheHeadingBandIsTreatedAsAnObstacle()
    {
        // The mechanism, asserted directly: a path crossing a heading band must be reported as hitting something.
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 9 } };
        page.Groups.Add(new Group { Id = "band", Name = "层", Kind = "layer", Order = 1, Members = { "a" } });
        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "band", Bounds = new Bounds(6, 2, 1.4, 0.6) });

        var diagram = new Diagram { Id = "t", Type = "architecture", Pages = { page } };
        DiagramLayout.Apply(diagram);

        var band = diagram.Pages[0].Groups.Single().Bounds!;
        double top = band.Y + band.H / 2;

        // Dropping through the heading *text*, which starts at the container's left padding — not through the
        // middle of the container, where the band is empty. My first version used `band.X` and the check correctly
        // returned false: the text is only 0.48" wide and sits at the left.
        double textX = band.X - band.W / 2 + DiagramLayout.ContainerPaddingX + 0.2;

        var through = new List<Point>
        {
            new(textX, top + 1),
            new(textX, top - 0.1)
        };

        Assert.True(OrthogonalRouter.CrossesHeading(diagram.Pages[0], through),
            "a segment dropping through the heading band was not detected");
    }
}
