using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Decorations pushed past the same page edge must keep their spacing.
///
/// Each block was clamped on its own, so everything that overflowed the top landed on the same
/// boundary value. Measured on examples/phase8/arch.ir.json: title authored at y=11.4 and subtitle
/// at y=10.8 — 0.6" apart — came out at 9.25 and 9.35, 0.1" apart with a 0.40" overlap.
///
/// The relative spacing is the author's intent. Moving the group as a unit preserves it while still
/// bringing everything inside the sheet.
/// </summary>
public sealed class DecorationGroupingTests
{
    private static Diagram TwoTitles(double y1, double y2, double pageHeight = 10)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = pageHeight },
            Nodes = { new Node { Id = "n", Text = "N", Bounds = new Bounds(5, 4, 1.4, 0.7) } },
            TextBlocks =
            {
                new TextBlock { Id = "title", Text = "Title", Role = "title", Bounds = new Bounds(5, y1, 4, 0.6) },
                new TextBlock { Id = "subtitle", Text = "Sub", Role = "note", Bounds = new Bounds(5, y2, 4, 0.4) }
            }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    private static double Gap(Diagram d)
    {
        var t = d.Pages[0].TextBlocks;
        return Math.Abs(t[0].Bounds.Y - t[1].Bounds.Y);
    }

    [Fact]
    public void SpacingSurvivesClampingAtTheTopEdge()
    {
        // Both authored above the sheet; their 0.6" separation must not collapse.
        var diagram = TwoTitles(y1: 11.4, y2: 10.8, pageHeight: 10);

        DecorationLayout.ClampToPage(diagram);

        Assert.Equal(0.6, Gap(diagram), 2);
    }

    [Fact]
    public void EverythingEndsUpOnThePage()
    {
        var diagram = TwoTitles(y1: 11.4, y2: 10.8, pageHeight: 10);

        DecorationLayout.ClampToPage(diagram);

        var page = diagram.Pages[0];
        foreach (var t in page.TextBlocks)
        {
            Assert.InRange(t.Bounds.Y - t.Bounds.H / 2, -0.01, page.Size!.Height);
            Assert.InRange(t.Bounds.Y + t.Bounds.H / 2, -0.01, page.Size.Height);
        }
    }

    [Fact]
    public void NoOverlapRemains()
    {
        var diagram = TwoTitles(y1: 11.4, y2: 10.8, pageHeight: 10);

        DecorationLayout.ClampToPage(diagram);

        Assert.DoesNotContain(LayoutAnalyzer.Analyze(diagram).Issues, i => i.Code == "text.overlaps-text");
    }

    [Fact]
    public void SpacingSurvivesTheBottomEdgeToo()
    {
        var diagram = TwoTitles(y1: -2.0, y2: -1.4, pageHeight: 10);

        DecorationLayout.ClampToPage(diagram);

        Assert.Equal(0.6, Gap(diagram), 2);
    }

    [Fact]
    public void ABlockAlreadyInsideIsNotMoved()
    {
        // Only the overflowing blocks move; an undisturbed one keeps its authored position.
        var diagram = TwoTitles(y1: 5.0, y2: 4.4, pageHeight: 10);
        var before = diagram.Pages[0].TextBlocks[0].Bounds.Y;

        DecorationLayout.ClampToPage(diagram);

        Assert.Equal(before, diagram.Pages[0].TextBlocks[0].Bounds.Y, 3);
    }
}
