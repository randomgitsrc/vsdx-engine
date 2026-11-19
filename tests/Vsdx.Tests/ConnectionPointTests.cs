using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every shape carries one connection point, at its centre. Every connector that ends on that shape therefore
/// lands on the same coordinate, and a node with three inbound edges gets three lines converging on one pixel.
///
/// The second visual review called this the most damaging scar in both diagrams: the API gateway's left corner
/// carried three lines and an arrowhead on the same point, drawn as a black knot. Measured in the package, the
/// gateway had **one** connection point at `(1.300, 0.275)` — its centre — not the four the review assumed.
///
/// A shape needs at least one point per side so edges arriving from different directions can land on different
/// coordinates.
/// </summary>
public sealed class ConnectionPointTests
{
    private static Page OneBox()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 10, Height = 8 } };
        page.Nodes.Add(new Node { Id = "a", Text = "服务", Bounds = new Bounds(5, 4, 2, 1) });
        return page;
    }

    [Fact]
    public void ABoxHasOnePointPerSide()
    {
        var page = OneBox();

        var points = ConnectionPointLayout.For(page.Nodes[0]);

        Assert.Equal(4, points.Count);

        // One on each edge midpoint, relative to the shape's own box.
        Assert.Contains(points, p => p.X < 0.01 && Math.Abs(p.Y - 0.5) < 0.01);   // left
        Assert.Contains(points, p => Math.Abs(p.X - 1.0) < 0.01 && Math.Abs(p.Y - 0.5) < 0.01); // right
        Assert.Contains(points, p => Math.Abs(p.X - 0.5) < 0.01 && p.Y < 0.01);   // bottom
        Assert.Contains(points, p => Math.Abs(p.X - 0.5) < 0.01 && Math.Abs(p.Y - 1.0) < 0.01); // top
    }

    [Fact]
    public void PointsAreRelativeNotAbsolute()
    {
        // Visio connection points are fractions of the shape's box; an absolute coordinate would scale with the
        // shape and drift.
        var node = new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 7, 4, 2) };

        var points = ConnectionPointLayout.For(node);

        Assert.All(points, p => Assert.InRange(p.X, 0, 1));
        Assert.All(points, p => Assert.InRange(p.Y, 0, 1));
    }

    [Fact]
    public void TwoInboundEdgesFromOppositeSidesGetDifferentPoints()
    {
        // The property that matters: two connectors arriving at one node must not share a coordinate.
        var points = ConnectionPointLayout.For(new Node { Id = "a", Text = "A", Bounds = new Bounds(5, 4, 2, 1) });

        var left = points.Single(p => p.X < 0.01);
        var right = points.Single(p => Math.Abs(p.X - 1.0) < 0.01);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void ADiamondGetsPointsAtItsVerticesNotItsBoxCorners()
    {
        // A diamond's box corners are outside the ink; a connector anchored there floats in empty space.
        var node = new Node { Id = "a", Text = "判断", Shape = "Diamond", Bounds = new Bounds(5, 4, 2, 2) };

        var points = ConnectionPointLayout.For(node);

        Assert.All(points, p =>
            Assert.True(Math.Abs(p.X - 0.5) < 0.01 || Math.Abs(p.Y - 0.5) < 0.01,
                $"diamond point ({p.X:F2},{p.Y:F2}) is not on a vertex axis"));
    }
}
