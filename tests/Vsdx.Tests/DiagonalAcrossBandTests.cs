using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `edge.diagonal-across-band` must be reachable through `Analyze`, which routes before it checks.
///
/// `OrthogonalRouter.Route` rewrites every edge to `Kind = "rightAngle"` and fills in waypoints, while the
/// check skips routed edges and skips right-angled ones. So by the time it runs, every edge is both — and
/// the check has never fired in any scenario (84 layout combinations produced it zero times).
///
/// The fix is to capture the author's geometry before routing rather than re-reading the model after it.
/// `Analyze` does that itself now, so callers see no signature change.
/// </summary>
public sealed class DiagonalAcrossBandTests
{
    /// <summary>An authored straight edge between two containers, clipping a third in between.</summary>
    private static Diagram CrossingStraight()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 16, Height = 12 } };

        page.Groups.Add(new Group { Id = "left", Name = "Left", Bounds = new Bounds(3, 8, 3, 2) });
        page.Groups.Add(new Group { Id = "middle", Name = "Middle", Bounds = new Bounds(8, 5.5, 3, 2) });
        page.Groups.Add(new Group { Id = "right", Name = "Right", Bounds = new Bounds(13, 3, 3, 2) });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "left", Bounds = new Bounds(3, 8, 1.2, 0.5) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "right", Bounds = new Bounds(13, 3, 1.2, 0.5) });

        page.Edges.Add(new Edge { Id = "e", From = "a", To = "b", Kind = "straight" });

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void AnAuthoredStraightCutIsReported()
    {
        var report = LayoutAnalyzer.Analyze(CrossingStraight());

        Assert.Contains(report.Issues, i => i.Code == "edge.diagonal-across-band");
    }

    [Fact]
    public void AnAuthoredRightAngleIsNotReported()
    {
        // The author asked for orthogonal routing; there is no diagonal to complain about.
        var diagram = CrossingStraight();
        diagram.Pages[0].Edges[0].Kind = "rightAngle";

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.diagonal-across-band");
    }

    [Fact]
    public void AnEdgeWithAuthoredWaypointsIsNotReported()
    {
        var diagram = CrossingStraight();
        diagram.Pages[0].Edges[0].Waypoints = new List<Point> { new(3, 3), new(13, 3) };

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.diagonal-across-band");
    }

    [Fact]
    public void AMissingMiddleContainerMeansNoClipping()
    {
        // Without a third container the straight run crosses only empty space.
        var diagram = CrossingStraight();
        diagram.Pages[0].Groups.RemoveAll(g => g.Id == "middle");

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.diagonal-across-band");
    }

    [Fact]
    public void AnEdgeThatOnlyCrossesContainersKeepsItsStraightRoute()
    {
        // The router only re-routes a straight edge when it would cut through another *shape*. This one
        // passes through a container but between the nodes, so it stays straight — which is exactly why
        // the diagonal check has to run: the reader sees a raw diagonal across a band.
        //
        // Capturing the author's geometry must not change that: the model after analysis still has the
        // edge the author wrote.
        var diagram = CrossingStraight();

        LayoutAnalyzer.Analyze(diagram);

        var edge = diagram.Pages[0].Edges[0];

        Assert.Equal("straight", edge.Kind);
        Assert.True(edge.Waypoints is null or { Count: 0 });
    }
}
