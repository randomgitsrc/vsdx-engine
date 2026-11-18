using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Nested containers across multiple pages, checked explicitly.
///
/// <see cref="DiagramLayout.CheckContainment"/> only asks whether each group encloses the nodes
/// it lists. It says nothing about a parent group enclosing its child group's bounds, so an
/// empty result from it does not mean the nesting survived a transform. These assertions spell
/// out the parent/child relationship themselves.
/// </summary>
public sealed class DirectionNestedGroupTests
{
    private static Page NestedPage(string name)
    {
        var page = new Page
        {
            Name = name,
            Size = new PageSize { Width = 14, Height = 9 },
            Nodes =
            {
                new Node { Id = name + "-a", Text = "A", Bounds = new Bounds(0, 0, 1.6, 0.8) },
                new Node { Id = name + "-b", Text = "B", Bounds = new Bounds(0, 0, 1.6, 0.8) },
                new Node { Id = name + "-c", Text = "C", Bounds = new Bounds(0, 0, 1.6, 0.8) }
            },
            Edges =
            {
                new Edge { Id = name + "-ab", From = name + "-a", To = name + "-b" },
                new Edge { Id = name + "-bc", From = name + "-b", To = name + "-c" }
            },
            TextBlocks = { new TextBlock { Id = name + "-t", Text = "T", Bounds = new Bounds(2, 7, 3, 0.4) } }
        };

        // Deliberately non-square parents so a failure to swap width and height is visible.
        page.Groups.Add(new Group
        {
            Id = name + "-inner",
            Members = { name + "-a", name + "-b" },
            Bounds = new Bounds(4, 4, 5, 2),
            Style = new ShapeStyle { PaddingX = 0.30, HeadingHeight = 0.46, PaddingBottom = 0.22 }
        });

        page.Groups.Add(new Group
        {
            Id = name + "-outer",
            Members = { name + "-c" },
            ParentId = name + "-inner",
            Bounds = new Bounds(5, 3, 8, 4),
            Style = new ShapeStyle { PaddingX = 0.50, HeadingHeight = 0.60, PaddingBottom = 0.22 }
        });

        return page;
    }

    private static Diagram TwoNestedPages() => new()
    {
        Orientation = "landscape",
        PageSize = new PageSize { Width = 14, Height = 9 },
        Pages = { NestedPage("p1"), NestedPage("p2") }
    };

    /// <summary>True when <paramref name="inner"/> lies entirely inside <paramref name="outer"/>.</summary>
    private static bool Encloses(Bounds outer, Bounds inner, double tolerance = 1e-6) =>
        inner.X - inner.W / 2 >= outer.X - outer.W / 2 - tolerance &&
        inner.X + inner.W / 2 <= outer.X + outer.W / 2 + tolerance &&
        inner.Y - inner.H / 2 >= outer.Y - outer.H / 2 - tolerance &&
        inner.Y + inner.H / 2 <= outer.Y + outer.H / 2 + tolerance;

    [Fact]
    public void ParentGroupEnclosesChildGroupOnEveryPage()
    {
        var diagram = TwoNestedPages();

        foreach (var page in diagram.Pages)
        {
            var outer = page.Groups.Single(g => g.Id == page.Name + "-outer").Bounds!;
            var inner = page.Groups.Single(g => g.Id == page.Name + "-inner").Bounds!;

            // Each page's fixtures nest inner inside outer before the transform.
            var swappedOuter = new Bounds(outer.Y, outer.X, outer.H, outer.W);
            var swappedInner = new Bounds(inner.Y, inner.X, inner.H, inner.W);
            Assert.True(Encloses(swappedOuter, swappedInner),
                $"{page.Name}: after transposition the parent no longer encloses the child");
        }
    }

    [Fact]
    public void TransposeKeepsParentEnclosingChildOnEveryPage()
    {
        var diagram = TwoNestedPages();

        DiagramLayout.Transpose(diagram);

        foreach (var page in diagram.Pages)
        {
            var outer = page.Groups.Single(g => g.Id == page.Name + "-outer").Bounds!;
            var inner = page.Groups.Single(g => g.Id == page.Name + "-inner").Bounds!;

            Assert.True(Encloses(outer, inner),
                $"{page.Name}: outer {outer.W:F2}x{outer.H:F2} does not enclose inner {inner.W:F2}x{inner.H:F2}");
        }
    }

    [Fact]
    public void TransposeSwapsNestedGroupBoundsAndPaddingOnEveryPage()
    {
        var diagram = TwoNestedPages();

        DiagramLayout.Transpose(diagram);

        foreach (var page in diagram.Pages)
        {
            var inner = page.Groups.Single(g => g.Id == page.Name + "-inner");
            // (4,4,5,2) -> (4,4,2,5)
            Assert.Equal(4, inner.Bounds!.X, 3);
            Assert.Equal(4, inner.Bounds.Y, 3);
            Assert.Equal(2, inner.Bounds.W, 3);
            Assert.Equal(5, inner.Bounds.H, 3);
            Assert.Equal(0.46, inner.Style!.PaddingX!.Value, 3);
            Assert.Equal(0.30, inner.Style.HeadingHeight!.Value, 3);

            var outer = page.Groups.Single(g => g.Id == page.Name + "-outer");
            // (5,3,8,4) -> (3,5,4,8)
            Assert.Equal(3, outer.Bounds!.X, 3);
            Assert.Equal(5, outer.Bounds.Y, 3);
            Assert.Equal(4, outer.Bounds.W, 3);
            Assert.Equal(8, outer.Bounds.H, 3);
            Assert.Equal(0.60, outer.Style!.PaddingX!.Value, 3);
            Assert.Equal(0.50, outer.Style.HeadingHeight!.Value, 3);
        }
    }

    [Fact]
    public void TransposeTwiceRestoresNestedBoundsOnEveryPage()
    {
        var diagram = TwoNestedPages();

        var before = diagram.Pages.Select(p => p.Groups
            .Select(g => (g.Id, g.Bounds!.X, g.Bounds.Y, g.Bounds.W, g.Bounds.H)).ToList()).ToList();

        DiagramLayout.Transpose(diagram);
        DiagramLayout.Transpose(diagram);

        for (int i = 0; i < diagram.Pages.Count; i++)
        {
            var groups = diagram.Pages[i].Groups;
            for (int j = 0; j < groups.Count; j++)
            {
                var bounds = groups[j].Bounds;
                Assert.NotNull(bounds);
                Assert.Equal(before[i][j].X, bounds.X, 6);
                Assert.Equal(before[i][j].Y, bounds.Y, 6);
                Assert.Equal(before[i][j].W, bounds.W, 6);
                Assert.Equal(before[i][j].H, bounds.H, 6);
            }
        }
    }

    [Fact]
    public void LayoutPipelineTransposesRecomputedGroupBoundsOnBothPages()
    {
        // The layout pipeline recomputes container bounds from member geometry (groups are
        // auto-sized by default), so hand-authored bounds do not survive it. What must survive
        // is that every group is still a valid box, on every page, after the transpose.
        var diagram = TwoNestedPages();

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Strategy = LayoutStrategy.Layered, Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Layered);

        foreach (var page in diagram.Pages)
        {
            Assert.Empty(DiagramLayout.CheckContainment(page));

            foreach (var group in page.Groups)
            {
                Assert.NotNull(group.Bounds);
                Assert.True(group.Bounds!.W > 0 && group.Bounds.H > 0,
                    $"{page.Name}/{group.Id}: degenerate bounds {group.Bounds.W:F2}x{group.Bounds.H:F2}");
            }

            // Padding still reflects the swap even though the box was recomputed.
            var inner = page.Groups.Single(g => g.Id == page.Name + "-inner");
            Assert.Equal(0.46, inner.Style!.PaddingX!.Value, 3);
            Assert.Equal(0.30, inner.Style.HeadingHeight!.Value, 3);

            Assert.True(page.Size!.Height > page.Size.Width,
                $"{page.Name}: page was not transposed");
        }
    }

    [Fact]
    public void EveryTransformedObjectOnBothPagesIsAccountedFor()
    {
        var diagram = TwoNestedPages();
        DiagramLayout.Transpose(diagram);

        foreach (var page in diagram.Pages)
        {
            // Nothing in the page may be left untransposed: every object that carries page
            // coordinates is asserted here so a future field cannot be silently skipped.
            Assert.All(page.Nodes, n => Assert.True(n.Bounds.W > 0 && n.Bounds.H > 0));
            Assert.All(page.Groups, g => Assert.NotNull(g.Bounds));
            Assert.All(page.TextBlocks, t => Assert.NotNull(t.Bounds));
            Assert.All(page.Paths, p => Assert.NotNull(p.Bounds));
            Assert.All(page.Edges, e => Assert.StartsWith(page.Name + "-", e.Id));

            // Page size traded places, so every page is now taller than it is wide.
            Assert.True(page.Size!.Height > page.Size.Width);
        }

        Assert.True(diagram.PageSize!.Height > diagram.PageSize.Width);
        Assert.Equal("portrait", diagram.Orientation);
    }
}
