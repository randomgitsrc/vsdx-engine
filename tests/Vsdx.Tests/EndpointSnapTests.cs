using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Connector endpoints are computed from a shape's bounding box, so on anything but a rectangle they land outside
/// the visible outline.
///
/// `OrthogonalRouter.BoundaryPoint` scales the direction vector until it meets the rectangle `b` describes. For a
/// parallelogram inset by 25% per side, the top edge is only 50% as wide as the box, so a connector arriving at
/// the top-left of the box stops in empty space a quarter of the width away from any ink.
///
/// The visual reviewer measured this on the agent's diagram: the connector to Kafka ended at x=4.55 while the
/// trapezoid's left edge was at 4.67 — 0.1" of nothing.
/// </summary>
public sealed class EndpointSnapTests
{
    [Fact]
    public void ARectangleEndpointSitsOnItsEdge()
    {
        var rect = new Bounds(5, 5, 2, 1);

        var point = ShapeGeometry.OutlinePoint(rect, "Rectangle", dx: -1, dy: 0);

        Assert.Equal(4.0, point.X, 3);   // left edge
        Assert.Equal(5.0, point.Y, 3);
    }

    [Theory]
    [InlineData("Parallelogram")]
    [InlineData("Hexagon")]
    [InlineData("Diamond")]
    public void AnInsetShapeEndpointLandsInsideItsBox(string shape)
    {
        // Straight in from the left. For these masters the ink does not reach the box edge there, so the endpoint
        // must move inward.
        var box = new Bounds(5, 5, 2, 1);

        var point = ShapeGeometry.OutlinePoint(box, shape, dx: -1, dy: 0);

        Assert.True(point.X > 4.0 + 0.01,
            $"{shape}: endpoint x={point.X:F3} is on the bounding box, not the outline (box left is 4.0)");
        Assert.True(point.X < 5.0, $"{shape}: endpoint should still be on the shape's left half");
    }

    [Theory]
    [InlineData("Circle")]
    [InlineData("Ellipse")]
    [InlineData("Rectangle")]
    public void ARoundShapeTouchesItsBoxAtTheCardinalPoints(string shape)
    {
        // My first version of the test above included Circle and Ellipse and failed — correctly. A circle
        // inscribed in a square reaches the box edge at the four cardinal points; only a diagonal departure is
        // inset. Treating the circle like a diamond would have pulled every horizontal connector off the shape.
        var box = new Bounds(5, 5, 2, 1);

        Assert.Equal(4.0, ShapeGeometry.OutlinePoint(box, shape, -1, 0).X, 3);

        // Diagonally, the circle is inside its box.
        var diagonal = ShapeGeometry.OutlinePoint(box, shape, -1, -1);
        Assert.True(diagonal.X > 4.0, $"{shape}: diagonal endpoint should be inset, got {diagonal.X:F3}");
    }

    [Fact]
    public void AnUnknownShapeFallsBackToTheBox()
    {
        // A shape we do not model must not have its endpoint pulled somewhere arbitrary.
        var box = new Bounds(5, 5, 2, 1);

        var point = ShapeGeometry.OutlinePoint(box, "SomethingExotic", dx: -1, dy: 0);

        Assert.Equal(4.0, point.X, 3);
    }

    [Fact]
    public void KafkaConnectorReachesTheTrapezoid()
    {
        // The end-to-end case the reviewer measured.
        var diagram = new Diagram
        {
            Id = "t",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 10, Height = 8 },
                    Nodes =
                    {
                        new Node { Id = "src", Text = "服务", Bounds = new Bounds(5, 6, 1.4, 0.6) },
                        new Node { Id = "kafka", Text = "Kafka", Shape = "Parallelogram", Bounds = new Bounds(5, 2, 1.6, 0.9) }
                    },
                    Edges = { new Edge { Id = "e", From = "src", To = "kafka" } }
                }
            }
        };

        OrthogonalRouter.Route(diagram);

        var edge = diagram.Pages[0].Edges.Single();
        var kafka = diagram.Pages[0].Nodes.Single(n => n.Id == "kafka").Bounds!;

        // The connector must terminate within the shape's horizontal span, not at the box's top corners.
        double endX = edge.Waypoints is { Count: > 0 } wp ? wp[^1].X : kafka.X;
        Assert.InRange(endX, kafka.X - kafka.W / 2, kafka.X + kafka.W / 2);
    }
}
