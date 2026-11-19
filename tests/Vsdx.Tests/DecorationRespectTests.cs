using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Decorations that state a position keep it; only the ones that state an intent are recomputed.
///
/// This is the line between two things that look alike. A title carries `align: center` — centring it after the
/// page shrinks honours what the author asked for. A note carrying only `x`/`y` and no alignment chose a place,
/// and moving it would override a decision rather than complete one.
///
/// The distinction came up while checking whether `cbo`'s 1.8" of empty bottom margin was a defect. It is not:
/// the note is at `y=0.6` because that is where it was put.
/// </summary>
public sealed class DecorationRespectTests
{
    [Fact]
    public void ALeftAlignedNoteKeepsItsAbsolutePosition()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 10, Height = 12 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(5, 8, 1.4, 0.6) });
        page.TextBlocks.Add(new TextBlock
        {
            Id = "note", Text = "注", Align = null,
            Bounds = new Bounds(3, 0.6, 4, 0.3)
        });

        var diagram = new Diagram { Id = "t", Pages = { page } };

        DecorationLayout.CentreTitles(diagram);
        DecorationLayout.AvoidNodes(diagram);

        var note = page.TextBlocks.Single();

        Assert.Equal(3, note.Bounds.X, 3);
        Assert.Equal(0.6, note.Bounds.Y, 3);
    }

    [Fact]
    public void AFootnotesGapToTheDrawingIsPartOfTheAuthoredLayout()
    {
        // The page grows to contain both, so the space between them is the author's choice, not slack the engine
        // failed to reclaim. Asserted so a future "tidy the page" change has to confront it.
        var page = new Page { Name = "p", Size = new PageSize { Width = 10, Height = 12 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(5, 8, 1.4, 0.6) });
        page.TextBlocks.Add(new TextBlock { Id = "note", Text = "注", Bounds = new Bounds(3, 0.6, 4, 0.3) });

        var diagram = new Diagram { Id = "t", Pages = { page } };

        DiagramSizer.FitPage(page, shrink: true);

        Assert.Equal(0.6, page.TextBlocks.Single().Bounds.Y, 3);
    }
}
