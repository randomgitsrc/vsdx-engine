using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A title should not be left lying across a band's border.
///
/// The clamp that keeps decorations on the page only consulted the page size, so a container that
/// grew during layout could expand into a title's area and nothing moved: measured on the
/// cross-border order diagram, a title at y 8.20-8.80 met a band reaching 8.76 and ended up
/// overlapping the band's own heading.
///
/// **The rule chosen: titles move up, out of the way.** A page title is the topmost element of the
/// document; overlapping a band has no meaning, unlike a zone label deliberately placed inside one.
/// Only text that straddles a border is nudged — text wholly inside a band stays where the author
/// put it.
/// </summary>
public sealed class DecorationContainerAvoidanceTests
{
    private static Diagram DiagramWith(double titleY, double groupTop, string? role = "title")
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 12 },
            Nodes = { new Node { Id = "a", Text = "A", Bounds = new Bounds(5, 5, 1.5, 0.7) } },
            TextBlocks =
            {
                new TextBlock
                {
                    Id = "title", Text = "Title", Role = role,
                    Bounds = new Bounds(5, titleY, 4, 0.6)
                }
            }
        };

        page.Groups.Add(new Group
        {
            Id = "band", Members = { "a" },
            Bounds = new Bounds(5, groupTop - 1, 6, 2)
        });

        return new Diagram { Id = "t", Pages = { page } };
    }

    private static double TopOf(TextBlock t) => t.Bounds.Y + t.Bounds.H / 2;
    private static double BottomOf(TextBlock t) => t.Bounds.Y - t.Bounds.H / 2;

    [Fact]
    public void ATitleStraddlingTheBandTopIsMovedClear()
    {
        // Band spans 6..8; title sits 7.7..8.3, half inside.
        var diagram = DiagramWith(titleY: 8.0, groupTop: 8);

        DecorationLayout.AvoidContainers(diagram);

        var title = diagram.Pages[0].TextBlocks[0];
        var band = diagram.Pages[0].Groups[0].Bounds!;

        Assert.True(BottomOf(title) >= band.Y + band.H / 2 - 0.01,
            $"title still reaches {BottomOf(title):F2} into a band topping out at {band.Y + band.H / 2:F2}");
    }

    [Fact]
    public void AZoneLabelEntirelyInsideItsBandIsLeftAlone()
    {
        // A label inside a band is the normal case; moving it would be wrong.
        var diagram = DiagramWith(titleY: 7.5, groupTop: 8, role: "note");

        var before = diagram.Pages[0].TextBlocks[0].Bounds.Y;
        DecorationLayout.AvoidContainers(diagram);

        Assert.Equal(before, diagram.Pages[0].TextBlocks[0].Bounds.Y, 3);
    }

    [Fact]
    public void MoveIsTheSmallestThatClears()
    {
        // Nudging by a hair rather than to the page edge: the title should stay near the top.
        var diagram = DiagramWith(titleY: 8.0, groupTop: 8);

        DecorationLayout.AvoidContainers(diagram);

        var title = diagram.Pages[0].TextBlocks[0];
        var band = diagram.Pages[0].Groups[0].Bounds!;

        double gap = BottomOf(title) - (band.Y + band.H / 2);
        Assert.InRange(gap, -0.01, 0.25);
    }

    [Fact]
    public void NothingHappensWhenThereIsNoOverlap()
    {
        var diagram = DiagramWith(titleY: 10, groupTop: 8);

        var before = diagram.Pages[0].TextBlocks[0].Bounds.Y;
        DecorationLayout.AvoidContainers(diagram);

        Assert.Equal(before, diagram.Pages[0].TextBlocks[0].Bounds.Y, 3);
    }

    [Fact]
    public void TheTitleStaysOnThePage()
    {
        // Moving up must not push it off the sheet; the page clamp still has the final say.
        var diagram = DiagramWith(titleY: 8.0, groupTop: 8);

        DecorationLayout.AvoidContainers(diagram);

        var title = diagram.Pages[0].TextBlocks[0];
        var page = diagram.Pages[0].Size!;

        Assert.InRange(TopOf(title), 0, page.Height);
    }

    [Fact]
    public void PathsAreLeftAlone()
    {
        // Free paths are drawn decoration; nudging one would detach it from what it annotates.
        var diagram = DiagramWith(titleY: 8.0, groupTop: 8);
        diagram.Pages[0].Paths.Add(new FreePath
        {
            Id = "rule", Bounds = new Bounds(5, 8.0, 6, 0.05)
        });

        var before = diagram.Pages[0].Paths[0].Bounds.Y;
        DecorationLayout.AvoidContainers(diagram);

        Assert.Equal(before, diagram.Pages[0].Paths[0].Bounds.Y, 3);
    }
}
