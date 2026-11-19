using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A page title stays where the author put it even after the page shrinks around the drawing.
///
/// `canvas.shrinkToFit` (on by default) trims the page to the content, but a text block carries an absolute
/// position chosen against the page the author imagined. Two of the four reference scenarios end up off-centre:
/// `cbo` at +0.72" and `user-data-flow` at -0.74".
///
/// `align: center` does not help — it centres the *text inside its box*, and the box is what moved relative to the
/// page.
///
/// The rule below is deliberately narrow: a block whose role is `title` or `subtitle` and whose alignment is
/// centre is re-centred on the page, because that is what the author asked for and what a reader expects. A block
/// with a stated left alignment keeps its x, because there the position is part of the request.
/// </summary>
public sealed class TitleCentringTests
{
    private static Diagram PageWithTitle(double pageWidth, double titleX, double titleWidth, string? align)
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = pageWidth, Height = 10 } };

        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(4, 5, 1.4, 0.6) });

        page.TextBlocks.Add(new TextBlock
        {
            Id = "title", Text = "标题", Role = "title", Align = align,
            Bounds = new Bounds(titleX, 9, titleWidth, 0.6)
        });

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void ACentredTitleIsCentredOnThePage()
    {
        var diagram = PageWithTitle(pageWidth: 8.55, titleX: 5.0, titleWidth: 6, align: "center");

        DecorationLayout.CentreTitles(diagram);

        var title = diagram.Pages[0].TextBlocks.Single();

        Assert.Equal(8.55 / 2, title.Bounds.X, 3);
    }

    [Fact]
    public void ALeftAlignedTitleKeepsItsPosition()
    {
        // The position is the request here; moving it would override a stated choice.
        var diagram = PageWithTitle(pageWidth: 8.55, titleX: 1.2, titleWidth: 3, align: "left");

        DecorationLayout.CentreTitles(diagram);

        Assert.Equal(1.2, diagram.Pages[0].TextBlocks.Single().Bounds.X, 3);
    }

    [Fact]
    public void ANoteIsNotCentred()
    {
        // Only the page-level headings. A footnote or caption sits where it was put.
        var page = new Page { Name = "p", Size = new PageSize { Width = 8.55, Height = 10 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(4, 5, 1.4, 0.6) });
        page.TextBlocks.Add(new TextBlock
        {
            Id = "note", Text = "注", Role = "note", Align = "center",
            Bounds = new Bounds(3, 1, 4, 0.3)
        });

        var diagram = new Diagram { Id = "t", Pages = { page } };

        DecorationLayout.CentreTitles(diagram);

        Assert.Equal(3, page.TextBlocks.Single().Bounds.X, 3);
    }

    [Fact]
    public void ATitleWiderThanThePageIsLeftAlone()
    {
        // Centring would push it off both edges. Keeping it where it is makes the overflow visible instead of
        // hiding half of it on each side.
        var diagram = PageWithTitle(pageWidth: 6, titleX: 3, titleWidth: 9, align: "center");

        DecorationLayout.CentreTitles(diagram);

        Assert.Equal(3, diagram.Pages[0].TextBlocks.Single().Bounds.X, 3);
    }

    [Fact]
    public void TheSubtitleFollowsTheTitle()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 8.55, Height = 10 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(4, 5, 1.4, 0.6) });
        page.TextBlocks.Add(new TextBlock { Id = "sub", Text = "副标题", Role = "subtitle", Align = "center", Bounds = new Bounds(6, 8.3, 3, 0.4) });

        var diagram = new Diagram { Id = "t", Pages = { page } };

        DecorationLayout.CentreTitles(diagram);

        Assert.Equal(8.55 / 2, page.TextBlocks.Single().Bounds.X, 3);
    }
}
