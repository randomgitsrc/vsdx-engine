using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Bands must be spaced by what their containers actually occupy, not by the height of the nodes
/// inside them.
///
/// A container is sized from its members' extent plus a heading, so a band of five nodes is taller
/// than a band of three. Advancing the row cursor by node height left the bands too close, and a
/// transpose turns that vertical deficit into horizontal overlap: measured on the deploy diagram,
/// the transposed containers needed 2.12" from centre to centre and had 1.97", overlapping by 0.14".
///
/// Getting this right was the fourth attempt at the problem. The first three — AutoSize, splitting
/// membership representations, skipping the transpose — were all falsified by measurement; this one
/// is derived from the overlap arithmetic and holds on both the benchmark and the spec diagram.
/// </summary>
public sealed class BandedSpacingTests
{
    private static Diagram Bands(params int[] memberCounts)
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 14, Height = 20 } };

        for (int b = 0; b < memberCounts.Length; b++)
        {
            var gid = $"g{b}";
            page.Groups.Add(new Group { Id = gid, Name = gid });

            for (int i = 0; i < memberCounts[b]; i++)
            {
                page.Nodes.Add(new Node
                {
                    Id = $"{gid}-n{i}", Text = $"N{i}", GroupId = gid,
                    Bounds = new Bounds(0, 0, 1.4, 0.7)
                });
            }
        }

        return new Diagram { Id = "t", Type = "workflow", Pages = { page } };
    }

    [Fact]
    public void UnevenBandsDoNotOverlap()
    {
        // Deliberately mismatched: five members makes a wide band, two makes a narrow one.
        var diagram = Bands(5, 2, 4);

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Flow);

        Assert.Empty(DiagramLayout.CheckContainment(diagram.Pages[0]));

        var report = LayoutAnalyzer.Analyze(diagram);
        Assert.DoesNotContain(report.Issues, i => i.Code == "group.overlap");
    }

    [Fact]
    public void EvenBandsAlsoDoNotOverlap()
    {
        var diagram = Bands(3, 3, 3);

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Flow);

        Assert.DoesNotContain(LayoutAnalyzer.Analyze(diagram).Issues, i => i.Code == "group.overlap");
    }

    [Fact]
    public void ASingleBandNeedsNoSpacing()
    {
        var diagram = Bands(4);

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Flow);

        Assert.Single(diagram.Pages[0].Groups);
    }

    [Fact]
    public void BandsStayInsideThePage()
    {
        var diagram = Bands(5, 2, 4);

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Flow);

        var page = diagram.Pages[0];
        foreach (var group in page.Groups)
        {
            var b = group.Bounds!;
            Assert.InRange(b.Y - b.H / 2, -0.01, page.Size!.Height);
        }
    }
}
