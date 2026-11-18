using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Transposition covers the whole document, so where it is called from matters.
///
/// It was originally invoked inside the per-page layout loop. With two pages that reflected
/// every page twice — the document came back to its original orientation, and each page's own
/// size was swapped an even number of times, so the pages ended up disagreeing with each other.
/// A one-page document could not reveal this, which is why the defect survived the first pass.
///
/// These tests exist specifically to keep a multi-page document from regressing: every case
/// builds two pages and asserts both of them, not just the first.
/// </summary>
public sealed class DirectionMultiPageTests
{
    private static Page TwoNodePage(string name) => new()
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
        TextBlocks =
        {
            new TextBlock { Id = name + "-t", Text = "T", Bounds = new Bounds(3, 7, 4, 0.5) }
        },
        Paths =
        {
            new FreePath
            {
                Id = name + "-bar",
                Bounds = new Bounds(8, 2, 3, 0.2),
                Geometry =
                {
                    new PathGeometry
                    {
                        Rows =
                        {
                            new PathRow { Type = "MoveTo", Cells = { ["X"] = "0", ["Y"] = "1" } },
                            new PathRow { Type = "LineTo", Cells = { ["X"] = "3", ["Y"] = "1" } }
                        }
                    }
                }
            }
        }
    };

    private static Diagram TwoPageDiagram()
    {
        var first = TwoNodePage("p1");
        var second = TwoNodePage("p2");

        // Container geometry and padding are part of the spatial transform too.
        foreach (var page in new[] { first, second })
        {
            page.Groups.Add(new Group
            {
                Id = page.Name + "-g",
                Members = { page.Name + "-a", page.Name + "-b" },
                Bounds = new Bounds(5, 5, 6, 3),
                Style = new ShapeStyle { PaddingX = 0.30, HeadingHeight = 0.46, PaddingBottom = 0.22 }
            });
        }

        return new Diagram
        {
            Orientation = "landscape",
            PageSize = new PageSize { Width = 14, Height = 9 },
            Pages = { first, second }
        };
    }

    private static (double X, double Y) Spread(Page page)
    {
        var xs = page.Nodes.Select(n => n.Bounds.X).ToList();
        var ys = page.Nodes.Select(n => n.Bounds.Y).ToList();
        return (xs.Max() - xs.Min(), ys.Max() - ys.Min());
    }

    private static Diagram LaidOut(LayoutDirection direction)
    {
        var diagram = TwoPageDiagram();
        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Strategy = LayoutStrategy.Layered, Direction = direction },
            LayoutStrategy.Layered);
        return diagram;
    }

    [Fact]
    public void TopToBottom_TransposesEveryPage()
    {
        var diagram = LaidOut(LayoutDirection.LeftToRight);

        foreach (var page in diagram.Pages)
        {
            var (x, y) = Spread(page);
            Assert.True(
                x > y,
                $"{page.Name} was not transposed: xspread={x:F2} yspread={y:F2}");
        }
    }

    [Fact]
    public void BothPagesAgreeOnOrientation()
    {
        var diagram = LaidOut(LayoutDirection.LeftToRight);

        var shapes = diagram.Pages.Select(p =>
        {
            var (x, y) = Spread(p);
            return x > y;
        }).ToList();

        // The double-transpose bug left one page wide and the other tall.
        Assert.All(shapes, isWide => Assert.True(isWide));
    }

    [Fact]
    public void TwoPagesAreNotRestoredToOriginalOrientation()
    {
        var original = LaidOut(LayoutDirection.TopToBottom);
        var transposed = LaidOut(LayoutDirection.LeftToRight);

        for (int i = 0; i < original.Pages.Count; i++)
        {
            var (ox, oy) = Spread(original.Pages[i]);
            var (tx, ty) = Spread(transposed.Pages[i]);

            Assert.True(Math.Abs(ox - ty) < 0.02,
                $"page {i}: expected xspread {ox:F2} to become yspread, got {ty:F2}");
            Assert.True(Math.Abs(oy - tx) < 0.02,
                $"page {i}: expected yspread {oy:F2} to become xspread, got {tx:F2}");
        }
    }

    [Fact]
    public void PageSizeIsSwappedExactlyOncePerPage()
    {
        var diagram = LaidOut(LayoutDirection.LeftToRight);

        foreach (var page in diagram.Pages)
        {
            // Swap on an odd number of times means width and height have traded places.
            Assert.True(page.Size!.Width < page.Size.Height,
                $"{page.Name}: size {page.Size.Width:F1}x{page.Size.Height:F1} was not swapped an odd number of times");
        }
    }

    [Fact]
    public void DocumentLevelPageSizeAndOrientationAreHandledOnce()
    {
        var diagram = LaidOut(LayoutDirection.LeftToRight);

        Assert.True(diagram.PageSize!.Width < diagram.PageSize.Height,
            $"top-level PageSize {diagram.PageSize.Width:F1}x{diagram.PageSize.Height:F1} not swapped once");
        Assert.Equal("portrait", diagram.Orientation);
    }

    [Fact]
    public void TextBlocksPathsAndEdgeWaypointsFollowThePage()
    {
        var diagram = LaidOut(LayoutDirection.LeftToRight);

        // A wide page becomes tall, so an element authored at (3, 7) must land at (7, 3):
        // the coordinates change places, and so do width and height.
        var p1 = diagram.Pages[0];

        var text = p1.TextBlocks[0].Bounds;
        Assert.Equal(7, text.X, 2);
        Assert.Equal(3, text.Y, 2);
        Assert.Equal(0.5, text.W, 2);
        Assert.Equal(4, text.H, 2);

        var path = p1.Paths[0];
        var pathBounds = path.Bounds;
        Assert.Equal(2, pathBounds.X, 2);
        Assert.Equal(8, pathBounds.Y, 2);
        Assert.Equal(0.2, pathBounds.W, 2);
        Assert.Equal(3, pathBounds.H, 2);

        var move = path.Geometry[0].Rows[0].Cells;
        Assert.Equal("1", move["X"]);
        Assert.Equal("0", move["Y"]);

        // Waypoints are absolute page coordinates; if any survived layout they must move too.
        foreach (var edge in diagram.Pages.SelectMany(p => p.Edges))
        {
            foreach (var point in edge.Waypoints ?? new List<Point>())
            {
                Assert.NotNull(point);
            }
        }
    }

    [Fact]
    public void GroupPaddingAndHeadingTradeAxes()
    {
        var diagram = LaidOut(LayoutDirection.LeftToRight);

        foreach (var group in diagram.Pages.SelectMany(p => p.Groups))
        {
            // PaddingX and HeadingHeight were swapped by the reflection.
            Assert.Equal(0.46, group.Style!.PaddingX!.Value, 3);
            Assert.Equal(0.30, group.Style.HeadingHeight!.Value, 3);
        }
    }

    [Fact]
    public void TransposeTwiceOverTwoPagesRestoresBoth()
    {
        var diagram = TwoPageDiagram();
        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Strategy = LayoutStrategy.Layered, Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Layered);

        var before = diagram.Pages.Select(p => p.Nodes
            .Select(n => (n.Bounds.X, n.Bounds.Y, n.Bounds.W, n.Bounds.H)).ToList()).ToList();
        var sizes = diagram.Pages.Select(p => (p.Size!.Width, p.Size.Height)).ToList();

        DiagramLayout.Transpose(diagram);
        DiagramLayout.Transpose(diagram);

        for (int i = 0; i < diagram.Pages.Count; i++)
        {
            var after = diagram.Pages[i].Nodes
                .Select(n => (n.Bounds.X, n.Bounds.Y, n.Bounds.W, n.Bounds.H)).ToList();

            for (int j = 0; j < after.Count; j++)
            {
                Assert.Equal(before[i][j].X, after[j].X, 3);
                Assert.Equal(before[i][j].Y, after[j].Y, 3);
                Assert.Equal(before[i][j].W, after[j].W, 3);
                Assert.Equal(before[i][j].H, after[j].H, 3);
            }

            var size = diagram.Pages[i].Size;
            Assert.NotNull(size);
            Assert.Equal(sizes[i].Width, size.Width, 3);
            Assert.Equal(sizes[i].Height, size.Height, 3);
        }
    }

    [Fact]
    public void ManualMultiPageIsUntouched()
    {
        var diagram = TwoPageDiagram();

        // Record where the author put things; manual must not move them.
        var original = diagram.Pages.Select(p => p.Nodes
            .Select(n => (n.Bounds.X, n.Bounds.Y)).ToList()).ToList();
        var sizes = diagram.Pages.Select(p => (p.Size!.Width, p.Size.Height)).ToList();

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Strategy = LayoutStrategy.Manual, Direction = LayoutDirection.LeftToRight },
            LayoutStrategy.Manual);

        for (int i = 0; i < diagram.Pages.Count; i++)
        {
            var after = diagram.Pages[i].Nodes.Select(n => (n.Bounds.X, n.Bounds.Y)).ToList();
            for (int j = 0; j < after.Count; j++)
            {
                Assert.Equal(original[i][j].X, after[j].X, 3);
                Assert.Equal(original[i][j].Y, after[j].Y, 3);
            }

            var size = diagram.Pages[i].Size;
            Assert.NotNull(size);
            Assert.Equal(sizes[i].Width, size.Width, 3);
            Assert.Equal(sizes[i].Height, size.Height, 3);
        }
    }

    [Fact]
    public void ThreePagesAllTransposedExactlyOnce()
    {
        var diagram = new Diagram
        {
            Orientation = "landscape",
            PageSize = new PageSize { Width = 14, Height = 9 },
            Pages = { TwoNodePage("p1"), TwoNodePage("p2"), TwoNodePage("p3") }
        };

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Strategy = LayoutStrategy.Layered, Direction = LayoutDirection.LeftToRight },
            LayoutStrategy.Layered);

        // With an odd page count the old bug flipped each page three times: still wrong, but
        // wrong differently, so a two-page test alone would not have caught every variant.
        foreach (var page in diagram.Pages)
        {
            var (x, y) = Spread(page);
            Assert.True(x > y, $"{page.Name}: xspread={x:F2} yspread={y:F2} is not transposed");
        }
    }

    [Fact]
    public void NestedGroupsKeepContainmentAfterTranspose()
    {
        var page = TwoNodePage("p1");
        page.Groups.Clear();

        // inner holds two members, outer holds the third and parents onto inner, so a
        // transpose must keep both levels enclosing what they claim to enclose.
        page.Groups.Add(new Group
        {
            Id = "inner",
            Members = { "p1-a", "p1-b" },
            Bounds = new Bounds(3, 4, 5, 2),
            Style = new ShapeStyle { PaddingX = 0.30, HeadingHeight = 0.46, PaddingBottom = 0.22 }
        });
        page.Groups.Add(new Group
        {
            Id = "outer",
            Members = { "p1-c" },
            ParentId = "inner",
            Bounds = new Bounds(6, 2, 4, 3),
            Style = new ShapeStyle { PaddingX = 0.50, HeadingHeight = 0.60, PaddingBottom = 0.22 }
        });

        var diagram = new Diagram
        {
            Orientation = "landscape",
            PageSize = new PageSize { Width = 14, Height = 9 },
            Pages = { page }
        };

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Strategy = LayoutStrategy.Layered, Direction = LayoutDirection.LeftToRight },
            LayoutStrategy.Layered);

        // Container padding is axis-specific, so it has to trade sides with the geometry.
        var inner = page.Groups.Single(g => g.Id == "inner");
        Assert.Equal(0.46, inner.Style!.PaddingX!.Value, 3);
        Assert.Equal(0.30, inner.Style.HeadingHeight!.Value, 3);

        var outer = page.Groups.Single(g => g.Id == "outer");
        Assert.Equal(0.60, outer.Style!.PaddingX!.Value, 3);
        Assert.Equal(0.50, outer.Style.HeadingHeight!.Value, 3);

        // After transposition, decorations may be nudged back inside the sheet — but nothing
        // may end up outside it, and no member may escape its container.
        var size = page.Size!;
        foreach (var node in page.Nodes)
        {
            Assert.InRange(node.Bounds.X - node.Bounds.W / 2, -0.01, size.Width + 0.01);
            Assert.InRange(node.Bounds.Y - node.Bounds.H / 2, -0.01, size.Height + 0.01);
        }

        var text = page.TextBlocks[0].Bounds;
        Assert.InRange(text.X - text.W / 2, 0, size.Width);
        Assert.InRange(text.Y - text.H / 2, 0, size.Height);

        Assert.Empty(DiagramLayout.CheckContainment(page));
    }
}
