using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// SizingTests: text must drive geometry, not be poured into geometry afterwards.
///
/// Phase 0 found that TextMetrics existed but nothing in the layout path called it, so node
/// sizes were hand-written constants unrelated to their labels. These tests lock in the
/// corrected direction of causality.
/// </summary>
public sealed class SizingTests
{
    private static Diagram Single(string text, double w = 2.0, double h = 0.8, double? fontSize = null)
    {
        var node = new Node { Id = "n", Text = text, Bounds = new Bounds(3, 5, w, h) };
        if (fontSize is not null) node.Style = new ShapeStyle { FontSize = fontSize };

        return new Diagram
        {
            PageSize = new PageSize { Width = 12, Height = 8 },
            Pages = { new Page { Id = "p", Name = "p", Size = new PageSize { Width = 12, Height = 8 }, Nodes = { node } } }
        };
    }

    [Fact]
    public void Sizer_GrowsNodeForLongText()
    {
        var diagram = Single("这是一个非常长的节点标题用于测试", w: 1.2, h: 0.5);
        var before = diagram.Pages[0].Nodes[0].Bounds.W;

        DiagramSizer.Apply(diagram);

        Assert.True(diagram.Pages[0].Nodes[0].Bounds.W > before,
            "a long CJK label must widen its node");
    }

    [Fact]
    public void Sizer_ShrinksNodeForShortText()
    {
        var diagram = Single("OK", w: 5.0, h: 2.0);

        DiagramSizer.Apply(diagram);

        var b = diagram.Pages[0].Nodes[0].Bounds;
        Assert.True(b.W < 5.0, "a short label must not keep a huge box");
        Assert.True(b.H < 2.0);
    }

    [Fact]
    public void Sizer_RespectsMinimumSize()
    {
        var diagram = Single("", w: 0.2, h: 0.2);

        DiagramSizer.Apply(diagram);

        var b = diagram.Pages[0].Nodes[0].Bounds;
        Assert.True(b.W >= DiagramSizer.MinNodeWidth);
        Assert.True(b.H >= DiagramSizer.MinNodeHeight);
    }

    [Fact]
    public void Sizer_ProducesNoOverflowAfterwards()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Nodes =
            {
                new Node { Id = "a", Text = "数据接入服务\nIngest Service", Bounds = new Bounds(3, 6, 1.0, 0.4) },
                new Node { Id = "b", Text = "查询/分析服务", Bounds = new Bounds(7, 6, 1.0, 0.4) },
                new Node { Id = "c", Text = "A considerably longer English caption", Bounds = new Bounds(11, 6, 1.0, 0.4) }
            }
        };

        var diagram = new Diagram { PageSize = page.Size, Pages = { page } };
        DiagramSizer.Apply(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);
        Assert.DoesNotContain(report.Issues, i => i.Code.StartsWith("text.overflow", StringComparison.Ordinal));
    }

    [Fact]
    public void Sizer_IsIdempotent()
    {
        var diagram = Single("幂等性测试 label", w: 0.8, h: 0.3);

        DiagramSizer.Apply(diagram);
        var first = (diagram.Pages[0].Nodes[0].Bounds.W, diagram.Pages[0].Nodes[0].Bounds.H);

        DiagramSizer.Apply(diagram);
        var second = (diagram.Pages[0].Nodes[0].Bounds.W, diagram.Pages[0].Nodes[0].Bounds.H);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Sizer_GrowsContainersAroundResizedMembers()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Groups = { new Group { Id = "g", Name = "Band", AutoSize = true } },
            Nodes =
            {
                new Node { Id = "a", Text = "很长的中文节点标题需要更多空间", Bounds = new Bounds(4, 6, 0.8, 0.3), GroupId = "g" },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 6, 0.8, 0.3), GroupId = "g" }
            }
        };
        page.Groups[0].Members.AddRange(new[] { "a", "b" });

        var diagram = new Diagram { PageSize = page.Size, Pages = { page } };
        var containerBefore = page.Groups[0].Bounds;

        DiagramSizer.Apply(diagram);
        var containerAfter = page.Groups[0].Bounds;

        Assert.NotNull(containerAfter);
        if (containerBefore is not null)
            Assert.True(containerAfter!.W >= containerBefore.W);
    }

    [Fact]
    public void FitPage_GrowsToFitContent()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 6, Height = 4 },
            Nodes = { new Node { Id = "a", Text = "x", Bounds = new Bounds(9, 7, 2, 1) } }
        };

        var fit = DiagramSizer.FitPage(page);

        Assert.True(fit.Grew);
        Assert.True(page.Size!.Width >= 9 + 1 - 0.01);
    }

    [Fact]
    public void FitPage_DoesNotShrinkWhenNotAsked()
    {
        // An author may deliberately target a paper size, so the low-level API grows only
        // unless trimming is requested explicitly.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 20, Height = 14 },
            Nodes = { new Node { Id = "a", Text = "x", Bounds = new Bounds(3, 5, 2, 1) } }
        };

        var fit = DiagramSizer.FitPage(page);

        Assert.False(fit.Grew);
        Assert.Equal(20, page.Size!.Width, 2);
    }

    [Fact]
    public void FitPage_TrimsWhenAsked()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 24, Height = 16 },
            Nodes = { new Node { Id = "a", Text = "x", Bounds = new Bounds(3, 5, 2, 1) } }
        };

        var fit = DiagramSizer.FitPage(page, shrink: true);

        Assert.True(fit.Shrank);
        Assert.True(page.Size!.Width < 24);
        Assert.True(page.Size.Width >= DiagramSizer.MinPageWidth);
    }

    [Fact]
    public void FitPage_KeepsFloorWhenTrimming()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 30, Height = 20 },
            Nodes = { new Node { Id = "a", Text = "x", Bounds = new Bounds(1, 1, 1, 0.5) } }
        };

        DiagramSizer.FitPage(page, shrink: true);

        Assert.True(page.Size!.Width >= DiagramSizer.MinPageWidth);
        Assert.True(page.Size.Height >= DiagramSizer.MinPageHeight);
    }

    [Fact]
    public void Recenter_KeepsEqualMargins()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 11 },
            Nodes = { new Node { Id = "a", Text = "x", Bounds = new Bounds(9, 8, 2, 1) } }
        };

        var (dx, dy) = DiagramSizer.Recenter(page, margin: 0.55);

        Assert.NotEqual(0, dx);
        Assert.NotEqual(0, dy);
        var b = page.Nodes[0].Bounds;
        Assert.Equal(DiagramSizer.PageMargin, b.X - b.W / 2, 1);
        Assert.Equal(DiagramSizer.PageMargin, b.Y - b.H / 2, 1);
    }

    [Fact]
    public void TextLayout_MeasuresCjkAsSquareGlyphs()
    {
        // A CJK glyph is one em wide; at 12pt that is 12/72 inch.
        double w = TextLayout.MeasureLine("中文", 12);
        Assert.Equal(2 * 12 / 72.0, w, 3);
    }

    [Fact]
    public void TextLayout_BoldIsWider()
    {
        Assert.True(TextLayout.MeasureLine("Hello", 11, 1.06) > TextLayout.MeasureLine("Hello", 11));
    }

    [Fact]
    public void TextLayout_WrapsLongLine()
    {
        var (text, measure) = TextLayout.Wrap(
            "This is a fairly long caption that should wrap onto more lines",
            maxWidthIn: 2.4, fontSize: 10);

        Assert.Contains("\n", text);
        Assert.True(measure.LineCount > 1);
        Assert.True(measure.RequiredWidth <= 2.4 + 0.05);
    }

    [Fact]
    public void TextLayout_WrapsCjkWithoutSpaces()
    {
        var (text, _) = TextLayout.Wrap("这是一个没有空格的很长的中文标题需要换行",
            maxWidthIn: 1.8, fontSize: 11);

        Assert.Contains("\n", text);
    }

    [Fact]
    public void TextLayout_PreservesAuthorLineBreaks()
    {
        var (text, measure) = TextLayout.Wrap("第一行\n第二行", maxWidthIn: 5.0, fontSize: 11);

        Assert.Equal(2, measure.LineCount);
        Assert.Contains("\n", text);
    }

    [Fact]
    public void TypeScale_StaysAboveReadableFloor()
    {
        // A dense, small canvas must not push type below the legibility floor.
        var scale = TypeScale.ForCanvas(widthIn: 6, heightIn: 4, nodeCount: 60);

        Assert.True(scale.NodeTitle >= TypeScale.MinReadablePt);
        Assert.True(scale.IsLegible);
    }

    [Fact]
    public void TypeScale_GrowsWithCanvas()
    {
        var small = TypeScale.ForCanvas(8, 6, 5);
        var large = TypeScale.ForCanvas(28, 18, 5);

        Assert.True(large.NodeTitle >= small.NodeTitle);
    }

    [Fact]
    public void TypeScale_HasDistinctTiers()
    {
        var scale = TypeScale.ForCanvas(18, 12, 15);

        Assert.True(scale.DiagramTitle > scale.RegionTitle);
        Assert.True(scale.RegionTitle > scale.NodeTitle);
        Assert.True(scale.NodeTitle >= scale.NodeCaption);
        Assert.True(scale.NodeCaption > scale.EdgeLabel);
    }

    [Fact]
    public void TypeScale_CapsBodySize()
    {
        var huge = TypeScale.ForCanvas(80, 60, 4);
        Assert.True(huge.NodeTitle <= TypeScale.MaxBodyPt);
    }
}
