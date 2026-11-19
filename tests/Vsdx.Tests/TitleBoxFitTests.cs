using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A heading's box can be far wider than its text, and `FitPage` sizes the page to the box — so the sheet carries
/// blank paper on the right that nothing occupies.
///
/// Measured on `cbo`: the authored box is 6.00" wide for eight characters, the drawing ends at 6.59", and the page
/// comes out 11.00" — 4.41" of it empty. Shrinking the box to the text would bring the page to about 7.15".
///
/// The box width is the author's choice, so shrinking it is a behaviour change: a heading deliberately given a wide
/// box so it can wrap stays as written.
/// </summary>
public sealed class TitleBoxFitTests
{
    private static Page PageWithWideTitle(string text, double width)
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };

        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 5, 1.4, 0.6) });

        page.TextBlocks.Add(new TextBlock
        {
            Id = "title", Text = text, Role = "title", Align = "center",
            Bounds = new Bounds(6, 9, width, 0.6)
        });

        return page;
    }

    [Fact]
    public void ATitleBoxNarrowsToItsText()
    {
        var page = PageWithWideTitle("跨境电商订单系统", 6.0);

        DiagramSizer.FitTextBlocks(new Diagram { Id = "t", Pages = { page } });

        // Eight CJK characters at 20pt are about 2.2" wide; the point is that 6.0 is gone, not the exact value.
        Assert.True(page.TextBlocks.Single().Bounds.W < 4.0,
            $"box still {page.TextBlocks.Single().Bounds.W:F2} wide");
    }

    [Fact]
    public void AHeadingThatAlreadyFitsKeepsItsBox()
    {
        // Narrower than the text needs is left alone, so the box must survive untouched.
        var page = PageWithWideTitle("短", 0.45);

        DiagramSizer.FitTextBlocks(new Diagram { Id = "t", Pages = { page } });

        Assert.Equal(0.45, page.TextBlocks.Single().Bounds.W, 3);
    }

    [Fact]
    public void ABoxNarrowerThanTheTextIsLeftToTheOverflowCheck()
    {
        // Only narrowing. Growing a box would move its left edge and could push it off the page — and a heading too
        // wide for its box is what `text.overflow-horizontal` reports, which is a better answer than silently
        // resizing something the author sized.
        var page = PageWithWideTitle("一个相当长的标题文本会用掉很多宽度", 0.5);

        DiagramSizer.FitTextBlocks(new Diagram { Id = "t", Pages = { page } });

        Assert.Equal(0.5, page.TextBlocks.Single().Bounds.W, 3);
    }

    [Fact]
    public void NotesAreLeftAlone()
    {
        // Only headings. A footnote box is a deliberate placement, and widening it would move its left edge.
        var page = PageWithWideTitle("注", 4.0);
        page.TextBlocks.Single().Role = "note";

        DiagramSizer.FitTextBlocks(new Diagram { Id = "t", Pages = { page } });

        Assert.Equal(4.0, page.TextBlocks.Single().Bounds.W, 3);
    }
}
