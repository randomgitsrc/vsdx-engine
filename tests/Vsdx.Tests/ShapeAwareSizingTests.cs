using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A circle's usable text area is smaller than its bounding box.
///
/// DiagramSizer measured every node as a rectangle, so a circular node sized from its label still
/// clipped: measured on a 14pt "Web 商城" in a 0.70x0.70 circle, the text needed 0.82" and the
/// analyser reported overflow — a hard failure once clipping became an error.
///
/// The inscribed rectangle of a circle is r*sqrt(2) per side, about 71% of the diameter; a diamond
/// is smaller still, since a rectangle inside it must satisfy w+h <= diagonal. Text therefore has
/// to be measured against the shape's usable area, not its bounding box.
///
/// **Growing the node is chosen over wrapping the text.** A circle in an architecture diagram
/// usually means an external actor, where a single readable line carries the meaning; wrapping
/// would push the shape toward a square and lose that. Wrapping remains for very long labels,
/// where an unbounded circle would be absurd.
/// </summary>
public sealed class ShapeAwareSizingTests
{
    private static Diagram Single(string shape, string text, double fontSize = 14) => new()
    {
        Id = "t",
        Pages =
        {
            new Page
            {
                Name = "p",
                Size = new PageSize { Width = 12, Height = 8 },
                Nodes =
                {
                    new Node
                    {
                        Id = "n", Text = text, Shape = shape,
                        Bounds = new Bounds(6, 4, 2, 1),
                        Style = new ShapeStyle { FontSize = fontSize }
                    }
                }
            }
        }
    };

    private static Node Size(Diagram d)
    {
        DiagramSizer.Apply(d);
        return d.Pages[0].Nodes[0];
    }

    [Fact]
    public void ACircleIsSizedWiderThanTheSameTextInARectangle()
    {
        // The whole point: identical text, identical font, and the circle needs more room because
        // its usable area is smaller.
        var circle = Size(Single("Circle", "Web 商城"));
        var rectangle = Size(Single("Rectangle", "Web 商城"));

        Assert.True(circle.Bounds.W > rectangle.Bounds.W + 0.05,
            $"circle {circle.Bounds.W:F2} should exceed rectangle {rectangle.Bounds.W:F2}");
    }

    [Fact]
    public void ACircleIsSizedSquare()
    {
        // A circle with unequal width and height renders as an ellipse; the shape name says circle.
        var node = Size(Single("Circle", "Web 商城"));

        Assert.Equal(node.Bounds.W, node.Bounds.H, 2);
    }

    [Fact]
    public void CircleLabelsNoLongerClip()
    {
        // The failure this fixes: text needed more width than the box had, reported as overflow.
        var diagram = Single("Circle", "Web 商城");
        var node = Size(diagram);

        var measured = TextLayout.Measure(node.Text, node.Style!.FontSize!.Value,
            node.Style.Bold ?? false, TypeScale.ForCanvas(12, 8, 1));

        // Allow the inscribed-area factor: usable width is smaller than the bounding width.
        double usable = node.Bounds.W * ShapeGeometry.InscribedFactor("Circle");
        Assert.True(measured.RequiredWidth <= usable + 0.02,
            $"text needs {measured.RequiredWidth:F2}\" but the inscribed area offers {usable:F2}\"");
    }

    [Fact]
    public void ARectangleIsUnaffected()
    {
        // Rectangles use their whole box; the fix must not inflate them.
        var node = Size(Single("Rectangle", "Web 商城"));
        var measured = TextLayout.Measure(node.Text, node.Style!.FontSize!.Value,
            node.Style.Bold ?? false, TypeScale.ForCanvas(12, 8, 1));

        Assert.True(node.Bounds.W >= measured.RequiredWidth - 0.01,
            $"rectangle {node.Bounds.W:F2} narrower than text {measured.RequiredWidth:F2}");
    }

    [Theory]
    [InlineData("Circle")]
    [InlineData("Ellipse")]
    [InlineData("Diamond")]
    [InlineData("Hexagon")]
    public void InscribedFactorsAreLessThanOne(string shape)
    {
        double factor = ShapeGeometry.InscribedFactor(shape);

        Assert.InRange(factor, 0.4, 1.0);
    }

    [Fact]
    public void UnknownShapesGetTheFullBox()
    {
        // A shape the geometry table does not know must not be penalised — assuming a constraint
        // it may not have would inflate every unrecognised master.
        Assert.Equal(1.0, ShapeGeometry.InscribedFactor("SomeCustomMaster"), 3);
        Assert.Equal(1.0, ShapeGeometry.InscribedFactor(null), 3);
    }

    [Fact]
    public void ADiamondNeedsMoreRoomThanACircle()
    {
        // A rectangle inside a diamond satisfies w+h <= diagonal, so it is tighter than a circle's
        // inscribed square.
        var diamond = Size(Single("Diamond", "Decision"));
        var circle = Size(Single("Circle", "Decision"));

        Assert.True(diamond.Bounds.W >= circle.Bounds.W - 0.01,
            $"diamond {diamond.Bounds.W:F2} should not be smaller than circle {circle.Bounds.W:F2}");
    }
}
