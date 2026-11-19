using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A client triangle and a service rectangle are sized by their label text alone, so the shape's own geometry
/// decides how much of the picture it occupies — and the least important element ends up the largest.
///
/// Measured on `cbo`: the client circles are 1.85" square (3.42 in²) while a service is 1.30 x 0.55 (0.72 in²).
/// **4.8x the area for an element that is peripheral by definition.** A reader's eye lands on the browser, not on
/// the order service, which is the opposite of what the diagram is for.
///
/// Two multipliers stack up: a sparse page gives 14pt body text, and a circle needs its diameter inflated by
/// 1/0.70 because only the inscribed square holds text.
///
/// The test below states the property rather than the mechanism: in a diagram with circles and rectangles, the
/// circle must not be the largest element on the page.
/// </summary>
public sealed class ShapeWeightTests
{
    private static Diagram ClientsAndServices()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 8.55, Height = 10 } };

        page.Groups.Add(new Group { Id = "top", Name = "接入层", Kind = "layer", Order = 2, Members = { "c1", "c2" } });
        page.Groups.Add(new Group { Id = "mid", Name = "服务层", Kind = "layer", Order = 1, Members = { "s1", "s2", "s3" } });

        // Nodes need a starting size or the sizer treats them as degenerate and leaves them at the minimums.
        // My first version omitted these and the test passed while measuring nothing.
        page.Nodes.Add(new Node { Id = "c1", Text = "Web 商城", Shape = "Circle", GroupId = "top", Bounds = new Bounds(3, 8, 2, 0.9) });
        page.Nodes.Add(new Node { Id = "c2", Text = "移动 App", Shape = "Circle", GroupId = "top", Bounds = new Bounds(5, 8, 2, 0.9) });
        page.Nodes.Add(new Node { Id = "s1", Text = "订单服务", GroupId = "mid", Bounds = new Bounds(2, 4, 2, 0.9) });
        page.Nodes.Add(new Node { Id = "s2", Text = "支付服务", GroupId = "mid", Bounds = new Bounds(4, 4, 2, 0.9) });
        page.Nodes.Add(new Node { Id = "s3", Text = "库存服务", GroupId = "mid", Bounds = new Bounds(6, 4, 2, 0.9) });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void TheClientIsNotTheLargestElement()
    {
        var diagram = ClientsAndServices();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        var nodes = diagram.Pages[0].Nodes;
        double clientArea = nodes.Where(n => n.Shape == "Circle").Max(n => n.Bounds.W * n.Bounds.H);
        double serviceArea = nodes.Where(n => n.Shape != "Circle").Max(n => n.Bounds.W * n.Bounds.H);

        Assert.True(clientArea <= serviceArea * 1.25,
            $"client {clientArea:F2} in² vs service {serviceArea:F2} in² — the peripheral element dominates");
    }

    [Fact]
    public void ASparsePageDoesNotInflateShapesWithoutLimit()
    {
        // The 14pt body on a sparse page is the other half of the problem: type that fills the paper also fills it
        // with boxes. A cap on the body size for shape sizing keeps the drawing proportionate.
        var scale = TypeScale.ForCanvas(8.55, 10, 5);

        Assert.True(scale.NodeTitle <= TypeScale.MaxBodyPt);
    }

    [Fact]
    public void ARoundShapeIsDrawnRound()
    {
        // `DiagramSizer` only intervenes when a box is too small for its text. A circle the author sized 2.0 x 0.9
        // fits its label, so nothing touched it and a round master rendered as an oval — no check reports an
        // aspect ratio, so it went unnoticed.
        var diagram = ClientsAndServices();

        // The sizer runs before layout in the real pipeline (`DesignSpec`), not inside it — the layout engine only
        // re-runs it after a transpose. Calling it here matches what `execute` does.
        DiagramSizer.Apply(diagram);

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        var circles = diagram.Pages[0].Nodes.Where(n => n.Shape == "Circle").ToList();
        Assert.NotEmpty(circles);

        Assert.All(circles, c => Assert.Equal(c.Bounds.W, c.Bounds.H, 3));
    }

    [Fact]
    public void TextStillFitsInsideTheCircle()
    {
        // The constraint that must not break: whatever the size, the label has to fit.
        var diagram = ClientsAndServices();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i =>
            i.Code is "text.overflow-horizontal" or "text.overflow-vertical");
    }
}
