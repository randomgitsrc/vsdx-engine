using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Decorations on every page, not just the first.
///
/// Node spread is a convenient proxy for "the page was transposed", but it says nothing about
/// whether text blocks, free paths or their outline geometry moved. Those are separate objects
/// with their own coordinates, and a transform that handles nodes while missing decorations
/// leaves a diagram whose parts disagree — so each page is checked object by object.
/// </summary>
public sealed class DirectionDecorationPerPageTests
{
    private static Page DecoratedPage(string name) => new()
    {
        Name = name,
        Size = new PageSize { Width = 14, Height = 9 },
        Nodes =
        {
            new Node { Id = name + "-a", Text = "A", Bounds = new Bounds(0, 0, 1.6, 0.8) },
            new Node { Id = name + "-b", Text = "B", Bounds = new Bounds(0, 0, 1.6, 0.8) }
        },
        Edges = { new Edge { Id = name + "-ab", From = name + "-a", To = name + "-b" } },
        TextBlocks =
        {
            // Distinct coordinates per page so a per-page mix-up cannot pass unnoticed.
            new TextBlock
            {
                Id = name + "-title",
                Text = "Title",
                Bounds = name == "p1" ? new Bounds(3, 7, 4, 0.5) : new Bounds(5, 6, 2, 0.4)
            },
            new TextBlock
            {
                Id = name + "-note",
                Text = "Note",
                Bounds = name == "p1" ? new Bounds(2, 1, 3, 0.3) : new Bounds(6, 2, 3, 0.3)
            }
        },
        Paths =
        {
            new FreePath
            {
                Id = name + "-bar",
                Bounds = name == "p1" ? new Bounds(8, 2, 3, 0.2) : new Bounds(9, 4, 2, 0.2),
                Geometry =
                {
                    new PathGeometry
                    {
                        Rows =
                        {
                            new PathRow { Type = "MoveTo", Cells = { ["X"] = "0", ["Y"] = "1" } },
                            new PathRow { Type = "LineTo", Cells = { ["X"] = "3", ["Y"] = "1" } },
                            new PathRow { Type = "LineTo", Cells = { ["X"] = "3", ["Y"] = "2" } }
                        }
                    }
                }
            }
        }
    };

    private static Diagram TwoDecoratedPages() => new()
    {
        Orientation = "landscape",
        PageSize = new PageSize { Width = 14, Height = 9 },
        Pages = { DecoratedPage("p1"), DecoratedPage("p2") }
    };

    [Fact]
    public void Transpose_SwapsTextBlockBoundsOnEveryPage()
    {
        var diagram = TwoDecoratedPages();

        // Before: p1 title (3,7,4,0.5), p2 title (5,6,2,0.4)
        DiagramLayout.Transpose(diagram);

        // The block moves with the sheet but keeps its proportions: writing stays horizontal, so a
        // 4.0 x 0.5 title must not come back as 0.5 x 4.0 — that is the shape that forced the words
        // to stack vertically on the transposed data-flow diagram.
        var p1 = diagram.Pages[0].TextBlocks.Single(t => t.Id == "p1-title").Bounds;
        Assert.Equal(7, p1.X, 3);
        Assert.Equal(3, p1.Y, 3);
        Assert.Equal(4, p1.W, 3);
        Assert.Equal(0.5, p1.H, 3);

        var p2 = diagram.Pages[1].TextBlocks.Single(t => t.Id == "p2-title").Bounds;
        Assert.Equal(6, p2.X, 3);
        Assert.Equal(5, p2.Y, 3);
        Assert.Equal(2, p2.W, 3);
        Assert.Equal(0.4, p2.H, 3);
    }

    [Fact]
    public void Transpose_SwapsSecondaryTextBlocksToo()
    {
        var diagram = TwoDecoratedPages();
        DiagramLayout.Transpose(diagram);

        var p1 = diagram.Pages[0].TextBlocks.Single(t => t.Id == "p1-note").Bounds;
        Assert.Equal(1, p1.X, 3);
        Assert.Equal(2, p1.Y, 3);

        var p2 = diagram.Pages[1].TextBlocks.Single(t => t.Id == "p2-note").Bounds;
        Assert.Equal(2, p2.X, 3);
        Assert.Equal(6, p2.Y, 3);
    }

    [Fact]
    public void Transpose_SwapsFreePathBoundsOnEveryPage()
    {
        var diagram = TwoDecoratedPages();
        DiagramLayout.Transpose(diagram);

        var p1 = diagram.Pages[0].Paths[0].Bounds;
        Assert.Equal(2, p1.X, 3);
        Assert.Equal(8, p1.Y, 3);
        Assert.Equal(0.2, p1.W, 3);
        Assert.Equal(3, p1.H, 3);

        var p2 = diagram.Pages[1].Paths[0].Bounds;
        Assert.Equal(4, p2.X, 3);
        Assert.Equal(9, p2.Y, 3);
        Assert.Equal(0.2, p2.W, 3);
        Assert.Equal(2, p2.H, 3);
    }

    [Fact]
    public void Transpose_SwapsGeometryCellsOnEveryPage()
    {
        var diagram = TwoDecoratedPages();
        DiagramLayout.Transpose(diagram);

        foreach (var page in diagram.Pages)
        {
            var rows = page.Paths[0].Geometry[0].Rows;

            // (X,Y) -> (Y,X) for every row, or the outline stops matching its own bounds.
            Assert.Equal("1", rows[0].Cells["X"]);
            Assert.Equal("0", rows[0].Cells["Y"]);

            Assert.Equal("1", rows[1].Cells["X"]);
            Assert.Equal("3", rows[1].Cells["Y"]);

            Assert.Equal("2", rows[2].Cells["X"]);
            Assert.Equal("3", rows[2].Cells["Y"]);
        }
    }

    [Fact]
    public void TransposeTwice_RestoresDecorationsOnEveryPage()
    {
        var diagram = TwoDecoratedPages();

        var before = diagram.Pages.Select(p => new
        {
            Texts = p.TextBlocks.Select(t => (t.Bounds.X, t.Bounds.Y, t.Bounds.W, t.Bounds.H)).ToList(),
            Paths = p.Paths.Select(f => (f.Bounds.X, f.Bounds.Y, f.Bounds.W, f.Bounds.H)).ToList(),
            Cells = p.Paths[0].Geometry[0].Rows
                .Select(r => (r.Cells["X"], r.Cells["Y"])).ToList()
        }).ToList();

        DiagramLayout.Transpose(diagram);
        DiagramLayout.Transpose(diagram);

        for (int i = 0; i < diagram.Pages.Count; i++)
        {
            var page = diagram.Pages[i];

            for (int j = 0; j < page.TextBlocks.Count; j++)
            {
                Assert.Equal(before[i].Texts[j].X, page.TextBlocks[j].Bounds.X, 6);
                Assert.Equal(before[i].Texts[j].Y, page.TextBlocks[j].Bounds.Y, 6);
                Assert.Equal(before[i].Texts[j].W, page.TextBlocks[j].Bounds.W, 6);
                Assert.Equal(before[i].Texts[j].H, page.TextBlocks[j].Bounds.H, 6);
            }

            for (int j = 0; j < page.Paths.Count; j++)
            {
                Assert.Equal(before[i].Paths[j].X, page.Paths[j].Bounds.X, 6);
                Assert.Equal(before[i].Paths[j].Y, page.Paths[j].Bounds.Y, 6);
            }

            var rows = page.Paths[0].Geometry[0].Rows;
            for (int j = 0; j < rows.Count; j++)
            {
                Assert.Equal(before[i].Cells[j].Item1, rows[j].Cells["X"]);
                Assert.Equal(before[i].Cells[j].Item2, rows[j].Cells["Y"]);
            }
        }
    }

    [Fact]
    public void BothPagesReportTransposedDecorationsViaLayoutPipeline()
    {
        // The pipeline clears waypoints but keeps decorations, so this confirms the practical
        // path also treats every page, not just the first.
        var diagram = TwoDecoratedPages();

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Strategy = LayoutStrategy.Layered, Direction = LayoutDirection.LeftToRight },
            LayoutStrategy.Layered);

        foreach (var page in diagram.Pages)
        {
            var size = page.Size!;

            // Text keeps its proportions: writing runs horizontally whatever the sheet does. A title
            // that came back taller than wide would have had to stack its characters vertically.
            var title = page.TextBlocks.Single(t => t.Id.EndsWith("-title")).Bounds;
            Assert.True(title.W > title.H,
                $"{page.Name}: title {title.W:F2}x{title.H:F2} should still be wider than tall");

            var path = page.Paths[0].Bounds;
            Assert.True(path.H > path.W,
                $"{page.Name}: path {path.W:F2}x{path.H:F2} was not transposed");

            // And nothing may end up off-sheet after the post-transpose clamp.
            Assert.InRange(title.X - title.W / 2, 0, size.Width);
            Assert.InRange(title.Y - title.H / 2, 0, size.Height);
        }
    }
}
