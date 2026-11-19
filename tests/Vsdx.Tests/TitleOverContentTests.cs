using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A heading is centred on the page, so when the drawing occupies the left part of a wide sheet the title floats
/// over empty space instead of over the diagram.
///
/// Measured on `cbo`: the page is 11.00" wide and the title sits at 5.50 — the page centre — while the drawing spans
/// `x[0.56, 6.59]` and its centre is 3.58. The title is **1.92" to the right of everything it describes**, with
/// 4.41" of blank paper beside it. The original defect this replaced was 0.72" off; centring on the sheet made it
/// worse and looked like a fix.
///
/// A reader looks at the drawing, not the paper. The heading belongs over the content.
/// </summary>
public sealed class TitleOverContentTests
{
    private static Diagram WidePageNarrowDrawing()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 14, Height = 10 } };

        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 5, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(4, 5, 1.4, 0.6) });

        page.TextBlocks.Add(new TextBlock
        {
            Id = "title", Text = "标题", Role = "title", Align = "center",
            Bounds = new Bounds(7, 9, 5, 0.6)
        });

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void TheTitleSitsOverTheDrawingNotOverTheSheet()
    {
        var diagram = WidePageNarrowDrawing();

        DecorationLayout.CentreTitles(diagram);

        var title = diagram.Pages[0].TextBlocks.Single();

        // The drawing spans 1.3 .. 4.7, so its centre is 3.0 — not the page's 7.
        Assert.Equal(3.0, title.Bounds.X, 2);
    }

    [Fact]
    public void ADrawingThatFillsThePageStillCentresOnThePage()
    {
        // When content and sheet agree there is no difference, and the rule must not move anything.
        var page = new Page { Name = "p", Size = new PageSize { Width = 10, Height = 8 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(1.5, 4, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(8.5, 4, 1.4, 0.6) });
        page.TextBlocks.Add(new TextBlock
        {
            Id = "title", Text = "标题", Role = "title", Align = "center",
            Bounds = new Bounds(7, 7, 3, 0.5)
        });

        var diagram = new Diagram { Id = "t", Pages = { page } };

        DecorationLayout.CentreTitles(diagram);

        // Content spans 0.8 .. 9.2, centre 5.0 — the same as the page centre here.
        Assert.Equal(5.0, diagram.Pages[0].TextBlocks.Single().Bounds.X, 2);
    }

    [Fact]
    public void TheTitleItselfIsExcludedFromTheContentBounds()
    {
        // Otherwise the title's own position decides where it should be — a fixed point that moves.
        var diagram = WidePageNarrowDrawing();

        DecorationLayout.CentreTitles(diagram);

        // Run twice: the second pass must not shift it again.
        var after = diagram.Pages[0].TextBlocks.Single().Bounds.X;
        DecorationLayout.CentreTitles(diagram);

        Assert.Equal(after, diagram.Pages[0].TextBlocks.Single().Bounds.X, 3);
    }

    [Fact]
    public void ALeftAlignedHeadingIsStillLeftAlone()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 14, Height = 10 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 5, 1.4, 0.6) });
        page.TextBlocks.Add(new TextBlock
        {
            Id = "title", Text = "标题", Role = "title", Align = "left",
            Bounds = new Bounds(1.2, 9, 3, 0.5)
        });

        var diagram = new Diagram { Id = "t", Pages = { page } };

        DecorationLayout.CentreTitles(diagram);

        Assert.Equal(1.2, page.TextBlocks.Single().Bounds.X, 3);
    }
}
