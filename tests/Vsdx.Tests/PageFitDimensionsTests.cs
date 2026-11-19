using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Growing one dimension must not pin the other at its old size.
///
/// `FitPage` decides `mustGrow` from width *or* height, and then applies `Math.Max(current, needed)` to both. So a
/// drawing that needs 0.13" more height keeps whatever width the page already had — even when the content is far
/// narrower.
///
/// This is what left `cbo` at 11.00" with 4.41" of blank paper. Traced with `VSDX_TRACE_FIT=1`:
///
///     [fit] grow=true current=11.00x9.32 needed=7.35x9.45
///
/// Width needed 7.35 against a current 11.00 — no growth required. Height needed 9.45 against 9.32 — 0.13" more.
/// The height triggered the branch and the width was carried along.
///
/// Three rounds of reading the code did not find this. One line of output did.
/// </summary>
public sealed class PageFitDimensionsTests
{
    private static Page TallAndNarrow()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 11.0, Height = 9.32 } };

        // Content 3" wide, 9.8" tall: the page is far too wide and just short of tall enough.
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(1.5, 1, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(1.5, 9, 1.4, 0.6) });

        return page;
    }

    [Fact]
    public void AHeightOverflowDoesNotKeepTheOldWidth()
    {
        var page = TallAndNarrow();

        DiagramSizer.FitPage(page, shrink: true);

        // Only the height was short. Width should come down to the content, not stay at 11.
        Assert.True(page.Size!.Width < 5.0,
            $"width stayed at {page.Size.Width:F2} while the content is about 3\" wide");

        Assert.True(page.Size.Height > 9.32, "height should have grown");
    }

    [Fact]
    public void AWidthOverflowDoesNotKeepTheOldHeight()
    {
        // The mirror case, so a fix cannot be one-sided.
        var page = new Page { Name = "p", Size = new PageSize { Width = 6.0, Height = 12.0 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(1, 6, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(9, 6, 1.4, 0.6) });

        DiagramSizer.FitPage(page, shrink: true);

        // `neededH` is the content's *top edge plus margin*, not its height: the nodes sit at y=6, so the page has
        // to reach 6.3 + 0.56. My first expectation of 6.0 was wrong about what the function computes, not about
        // what it should do.
        Assert.True(page.Size!.Height < 8.0,
            $"height stayed at {page.Size.Height:F2} while the content's top edge is at 6.3");
        Assert.True(page.Size.Width > 9.0, "width should have grown");
    }

    [Fact]
    public void ADrawingThatFitsBothWaysIsUntouched()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 8.0, Height = 6.0 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(4, 3, 1.4, 0.6) });

        var fit = DiagramSizer.FitPage(page, shrink: false);

        Assert.Equal(8.0, page.Size!.Width, 3);
        Assert.Equal(6.0, page.Size.Height, 3);
        Assert.False(fit.Shrank);
    }
}
