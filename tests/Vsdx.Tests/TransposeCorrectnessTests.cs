using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A transposed page has to read in the requested direction, with its text upright.
///
/// Two defects surfaced once leftToRight genuinely transposed:
///
///   1. **Direction reversed.** Still open: the engine stacks bands upward, and swapping X/Y turns
///      "up" into "left", so a transposed flow reads right-to-left — measured, `web` at x=21.82 and
///      `bi` at x=2.27 on a diagram whose IR reads web -> ... -> bi. Mirroring X fixes the order but
///      composes with the swap into a transform that is no longer its own inverse, and callers rely
///      on transposing twice returning the original. Recorded as a separate decision, not fixed.
///
///   2. **Text turned on its side.** Text blocks have no angle, so the words could not rotate —
///      instead the box itself was swapped: a title 0.60" wide by 7.00" tall became 7.00" wide by
///      0.60" tall, and the only way to fit a horizontal title into a 0.60" column is to stack the
///      characters vertically. Measured on the data-flow diagram, the heading ran top to bottom.
///
/// The fix mirrors X after the swap (so the flow reads left-to-right) and swaps a text block's
/// position while leaving its width and height alone.
/// </summary>
public sealed class TransposeCorrectnessTests
{
    private static Diagram Chain()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 9 },
            TextBlocks =
            {
                new TextBlock { Id = "title", Text = "Title", Role = "title", Bounds = new Bounds(4, 7, 4, 0.6) }
            }
        };

        for (int i = 0; i < 4; i++)
        {
            page.Nodes.Add(new Node { Id = $"n{i}", Text = $"N{i}", Bounds = new Bounds(0, 0, 1.4, 0.7) });
            if (i > 0) page.Edges.Add(new Edge { Id = $"e{i}", From = $"n{i - 1}", To = $"n{i}" });
        }

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void TextBlocksStayUpright()
    {
        var diagram = Chain();
        var before = diagram.Pages[0].TextBlocks[0].Bounds;

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.LeftToRight },
            LayoutStrategy.Layered);

        var after = diagram.Pages[0].TextBlocks[0].Bounds;

        // Writing runs horizontally whatever the page orientation: the box may move, but a title
        // that was 4x0.6 must not become 0.6x4.
        Assert.Equal(before.W, after.W, 2);
        Assert.Equal(before.H, after.H, 2);
    }

    [Fact]
    public void AShapeThatWasRotatedStillPointsTheSameWay()
    {
        // The angle mapping exists for shapes with their own rotation, and must keep working.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 8 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 4, 1.4, 0.7), Angle = 45 }
            }
        };
        var diagram = new Diagram { Id = "t", Pages = { page } };

        DiagramLayout.Transpose(diagram);

        Assert.Equal(45, page.Nodes[0].Angle!.Value, 1);
    }

    [Fact]
    public void ContainersFollowTheirMembersAfterMirroring()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 }
        };
        page.Groups.Add(new Group { Id = "g", Bounds = new Bounds(5, 4, 6, 2) });
        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "g", Bounds = new Bounds(3, 4, 1.4, 0.7) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "g", Bounds = new Bounds(5, 4, 1.4, 0.7) });

        var diagram = new Diagram { Id = "t", Pages = { page } };
        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.LeftToRight },
            LayoutStrategy.Layered);

        Assert.Empty(DiagramLayout.CheckContainment(diagram.Pages[0]));
    }
}
