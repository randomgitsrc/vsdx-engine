using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Edge geometry under transposition, exercised directly on <see cref="DiagramLayout.Transpose"/>.
///
/// These cannot go through <c>DiagramLayoutEngine.Apply</c>: it clears <c>Edge.Waypoints</c>
/// before laying out, on the grounds that author waypoints describe routes between positions
/// that no longer exist once nodes move. That is correct behaviour and is left alone — but it
/// means any waypoint assertion written against the layout pipeline checks an empty list and
/// proves nothing. Calling the transform directly is the only way to observe it.
/// </summary>
public sealed class DirectionEdgeGeometryTests
{
    private static Diagram EdgeGeometryDiagram()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(1, 2, 1.6, 0.8) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(9, 6, 1.6, 0.8) }
            },
            Edges =
            {
                new Edge
                {
                    Id = "ab",
                    From = "a",
                    To = "b",
                    FromPoint = new Point(1.8, 2.4),
                    ToPoint = new Point(8.2, 5.6),
                    Waypoints = new List<Point>
                    {
                        new(3.0, 4.0),
                        new(5.5, 4.0),
                        new(5.5, 6.5)
                    }
                }
            }
        };

        return new Diagram
        {
            Orientation = "landscape",
            PageSize = new PageSize { Width = 14, Height = 9 },
            Pages = { page }
        };
    }

    [Fact]
    public void Transpose_SwapsFromPointAndToPoint()
    {
        var diagram = EdgeGeometryDiagram();
        var edge = diagram.Pages[0].Edges[0];

        DiagramLayout.Transpose(diagram);

        Assert.Equal(2.4, edge.FromPoint!.X, 3);
        Assert.Equal(1.8, edge.FromPoint.Y, 3);
        Assert.Equal(5.6, edge.ToPoint!.X, 3);
        Assert.Equal(8.2, edge.ToPoint.Y, 3);
    }

    [Fact]
    public void Transpose_SwapsEveryWaypoint()
    {
        var diagram = EdgeGeometryDiagram();
        var edge = diagram.Pages[0].Edges[0];

        DiagramLayout.Transpose(diagram);

        var points = edge.Waypoints;
        Assert.NotNull(points);
        Assert.Equal(3, points.Count);

        // (3.0,4.0) (5.5,4.0) (5.5,6.5) -> (4.0,3.0) (4.0,5.5) (6.5,5.5)
        Assert.Equal(4.0, points[0].X, 3);
        Assert.Equal(3.0, points[0].Y, 3);
        Assert.Equal(4.0, points[1].X, 3);
        Assert.Equal(5.5, points[1].Y, 3);
        Assert.Equal(6.5, points[2].X, 3);
        Assert.Equal(5.5, points[2].Y, 3);
    }

    [Fact]
    public void TransposeTwice_RestoresEdgeGeometryExactly()
    {
        var diagram = EdgeGeometryDiagram();
        var edge = diagram.Pages[0].Edges[0];

        var fromBefore = (edge.FromPoint!.X, edge.FromPoint.Y);
        var toBefore = (edge.ToPoint!.X, edge.ToPoint.Y);
        var pointsBefore = edge.Waypoints!.Select(p => (p.X, p.Y)).ToList();

        DiagramLayout.Transpose(diagram);
        DiagramLayout.Transpose(diagram);

        Assert.Equal(fromBefore, (edge.FromPoint!.X, edge.FromPoint.Y));
        Assert.Equal(toBefore, (edge.ToPoint!.X, edge.ToPoint.Y));

        var pointsAfter = edge.Waypoints!.Select(p => (p.X, p.Y)).ToList();
        Assert.Equal(pointsBefore.Count, pointsAfter.Count);
        for (int i = 0; i < pointsBefore.Count; i++)
        {
            Assert.Equal(pointsBefore[i].X, pointsAfter[i].X, 6);
            Assert.Equal(pointsBefore[i].Y, pointsAfter[i].Y, 6);
        }
    }

    [Fact]
    public void Transpose_HandlesEdgeWithoutEndpointsOrWaypoints()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 8 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(1, 1, 1, 1) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(5, 5, 1, 1) }
            },
            Edges = { new Edge { Id = "ab", From = "a", To = "b" } }
        };
        var diagram = new Diagram { Pages = { page } };
        var edge = page.Edges[0];

        // Rather than throwing on a connector that carries no explicit geometry.
        DiagramLayout.Transpose(diagram);

        Assert.Null(edge.FromPoint);
        Assert.Null(edge.ToPoint);
        Assert.Null(edge.Waypoints);
    }

    [Fact]
    public void Transpose_LeavesWaypointsOnEveryEdgeOfEveryPage()
    {
        var first = EdgeGeometryDiagram().Pages[0];
        var second = EdgeGeometryDiagram().Pages[0];
        second.Name = "p2";
        second.Edges[0].Id = "ab2";

        var diagram = new Diagram
        {
            Orientation = "landscape",
            PageSize = new PageSize { Width = 14, Height = 9 },
            Pages = { first, second }
        };

        DiagramLayout.Transpose(diagram);

        foreach (var page in diagram.Pages)
        {
            var edge = page.Edges[0];
            Assert.NotNull(edge.Waypoints);
            Assert.Equal(3, edge.Waypoints.Count);
            Assert.Equal(4.0, edge.Waypoints[0].X, 3);
            Assert.Equal(3.0, edge.Waypoints[0].Y, 3);
        }
    }
}
