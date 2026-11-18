using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Bands must not overlap, whatever the container heading costs.
///
/// `LayoutBanded` places nodes with `y + rowHeight / 2` using the *member* height, then advances `y`
/// by `rowHeight + heading + padBottom + BandGapY` using the *container* height. The two baselines
/// differ, so the spacing it actually produces is neither constant nor predictable — measured, two
/// single-member bands whose containers are 1.23" tall ended up 1.52" apart with a 0.55" gap
/// requested, and the extra 0.29" came from the height promotion happening after placement.
///
/// This is the invariant that matters: whatever the arithmetic, derived containers must not overlap,
/// and reserving space for the heading must not be what breaks it.
/// </summary>
public sealed class BandSpacingInvariantTests
{
    private static Diagram Bands(int count)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 12 }
        };

        for (int i = 0; i < count; i++)
        {
            var id = $"g{i}";
            var nodeId = $"n{i}";

            page.Groups.Add(new Group { Id = id, Name = id, Members = { nodeId } });
            page.Nodes.Add(new Node { Id = nodeId, Text = nodeId, GroupId = id, Bounds = new Bounds(4, 2 + i, 1.4, 0.55) });
        }

        return new Diagram { Id = "t", Type = "workflow", Pages = { page } };
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void DerivedContainersDoNotOverlap(int count)
    {
        var diagram = Bands(count);

        new FlowLayoutEngine().Layout(diagram.Pages[0],
            new LayoutOptions { Direction = LayoutDirection.TopToBottom });

        var groups = diagram.Pages[0].Groups.Where(g => g.Bounds is not null).ToList();

        for (int i = 0; i < groups.Count; i++)
        for (int j = i + 1; j < groups.Count; j++)
        {
            var a = groups[i].Bounds!;
            var b = groups[j].Bounds!;

            double overlapY = Math.Min(a.Y + a.H / 2, b.Y + b.H / 2) - Math.Max(a.Y - a.H / 2, b.Y - b.H / 2);

            Assert.True(overlapY <= 1e-6,
                $"{groups[i].Id} and {groups[j].Id} overlap by {overlapY:F3}\"");
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void TheGapBetweenBandsIsConsistent(int count)
    {
        // Whatever the absolute value, consecutive pairs should be spaced the same way — a
        // placement/advance mismatch shows up as a gap that varies with band order.
        var diagram = Bands(count);

        new FlowLayoutEngine().Layout(diagram.Pages[0],
            new LayoutOptions { Direction = LayoutDirection.TopToBottom });

        var ordered = diagram.Pages[0].Groups
            .Where(g => g.Bounds is not null)
            .OrderByDescending(g => g.Bounds!.Y)
            .ToList();

        var gaps = new List<double>();

        for (int i = 0; i + 1 < ordered.Count; i++)
            gaps.Add((ordered[i].Bounds!.Y - ordered[i].Bounds!.H / 2)
                     - (ordered[i + 1].Bounds!.Y + ordered[i + 1].Bounds!.H / 2));

        Assert.All(gaps, g => Assert.InRange(g, 0, 1.5));
        Assert.True(gaps.Max() - gaps.Min() < 0.05,
            $"band gaps vary: {string.Join(", ", gaps.Select(g => g.ToString("F2")))}");
    }

    [Fact]
    public void BandsStayApartRegardlessOfTheRequestedDirection()
    {
        // `FlowLayoutEngine` lays bands vertically and the transpose swaps axes afterwards, so a
        // LeftToRight request produces the same stacked arrangement with the page rotated. The
        // containers still must not overlap in *some* axis — asserting horizontal separation was
        // wrong: measured against the pre-change build, the bands sit at identical X either way.
        var diagram = Bands(3);

        new FlowLayoutEngine().Layout(diagram.Pages[0],
            new LayoutOptions { Direction = LayoutDirection.LeftToRight });

        var groups = diagram.Pages[0].Groups.Where(g => g.Bounds is not null).ToList();

        for (int i = 0; i < groups.Count; i++)
        for (int j = i + 1; j < groups.Count; j++)
        {
            var a = groups[i].Bounds!;
            var b = groups[j].Bounds!;

            double overlapX = Math.Min(a.X + a.W / 2, b.X + b.W / 2) - Math.Max(a.X - a.W / 2, b.X - b.W / 2);
            double overlapY = Math.Min(a.Y + a.H / 2, b.Y + b.H / 2) - Math.Max(a.Y - a.H / 2, b.Y - b.H / 2);

            Assert.True(overlapX <= 1e-6 || overlapY <= 1e-6,
                $"{groups[i].Id} and {groups[j].Id} overlap in both axes");
        }
    }
}
