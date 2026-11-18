using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// LayoutDirection was a dead field: the enum existed, LayoutOptions carried it, the spec
/// applier passed it down, and no engine ever read it. Every strategy produced byte-identical
/// geometry for both directions.
///
/// These tests pin the behaviour that was missing, in both directions: top-to-bottom must
/// actually transpose, and manual must not.
/// </summary>
public sealed class DirectionTests
{
    private static Diagram Chain()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(0, 0, 1.6, 0.8) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(0, 0, 1.6, 0.8) },
                new Node { Id = "c", Text = "C", Bounds = new Bounds(0, 0, 1.6, 0.8) }
            },
            Edges =
            {
                new Edge { Id = "ab", From = "a", To = "b" },
                new Edge { Id = "bc", From = "b", To = "c" }
            }
        };

        return new Diagram { Pages = { page } };
    }

    private static (double X, double Y) Spread(Diagram diagram)
    {
        var nodes = diagram.Pages[0].Nodes;
        return (
            nodes.Max(n => n.Bounds.X) - nodes.Min(n => n.Bounds.X),
            nodes.Max(n => n.Bounds.Y) - nodes.Min(n => n.Bounds.Y));
    }

    [Theory]
    [InlineData(LayoutStrategy.Layered)]
    [InlineData(LayoutStrategy.Flow)]
    [InlineData(LayoutStrategy.Tree)]
    [InlineData(LayoutStrategy.Grid)]
    public void TopToBottom_SwapsTheSpreadForEveryStrategy(LayoutStrategy strategy)
    {
        var wide = Chain();
        DiagramLayoutEngine.Apply(wide, new LayoutOptions
        {
            Strategy = strategy,
            Direction = LayoutDirection.TopToBottom
        }, strategy);

        var tall = Chain();
        DiagramLayoutEngine.Apply(tall, new LayoutOptions
        {
            Strategy = strategy,
            Direction = LayoutDirection.LeftToRight
        }, strategy);

        var (wideX, wideY) = Spread(wide);
        var (tallX, tallY) = Spread(tall);

        // The whole point of the defect: these used to be identical.
        Assert.True(
            Math.Abs(wideX - tallY) < 0.02 && Math.Abs(wideY - tallX) < 0.02,
            $"{strategy}: expected spread to swap, got wide=({wideX:F2},{wideY:F2}) tall=({tallX:F2},{tallY:F2})");
    }

    [Fact]
    public void LeftToRightIsNotAnIdentity()
    {
        // LeftToRight is the direction that transposes, so it must differ from the engine's own
        // vertical default. Both are stated explicitly: relying on the default for the "before"
        // case made this compare a direction against itself once the semantics were corrected.
        var vertical = Chain();
        DiagramLayoutEngine.Apply(vertical, new LayoutOptions
        {
            Strategy = LayoutStrategy.Layered,
            Direction = LayoutDirection.TopToBottom
        });

        var horizontal = Chain();
        DiagramLayoutEngine.Apply(horizontal, new LayoutOptions
        {
            Strategy = LayoutStrategy.Layered,
            Direction = LayoutDirection.LeftToRight
        });

        var a = vertical.Pages[0].Nodes.Select(n => (Math.Round(n.Bounds.X, 2), Math.Round(n.Bounds.Y, 2))).ToList();
        var b = horizontal.Pages[0].Nodes.Select(n => (Math.Round(n.Bounds.X, 2), Math.Round(n.Bounds.Y, 2))).ToList();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Manual_LeavesAuthorCoordinatesAlone()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 6, 1.6, 0.8) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 3, 1.6, 0.8) }
            }
        };
        var diagram = new Diagram { Pages = { page } };

        DiagramLayoutEngine.Apply(diagram, new LayoutOptions
        {
            Strategy = LayoutStrategy.Manual,
            Direction = LayoutDirection.LeftToRight
        }, LayoutStrategy.Manual);

        Assert.Equal(2, page.Nodes[0].Bounds.X, 3);
        Assert.Equal(6, page.Nodes[0].Bounds.Y, 3);
        Assert.Equal(8, page.Nodes[1].Bounds.X, 3);
        Assert.Equal(3, page.Nodes[1].Bounds.Y, 3);
    }

    [Fact]
    public void ManualIgnoresDirectionWhicheverIsAsked()
    {
        // Manual keeps the author's coordinates, so neither direction can be honoured.
        foreach (var direction in new[] { LayoutDirection.LeftToRight, LayoutDirection.TopToBottom })
        {
            var options = new LayoutOptions { Strategy = LayoutStrategy.Manual, Direction = direction };

            Assert.True(DiagramLayoutEngine.IsDirectionIgnoredUnderManual(options, LayoutStrategy.Manual));
            Assert.False(DiagramLayoutEngine.IsDirectionIgnoredUnderManual(options, LayoutStrategy.Layered));
        }
    }

    [Fact]
    public void Transpose_IsItsOwnInverse()
    {
        var diagram = Chain();
        DiagramLayoutEngine.Apply(diagram, new LayoutOptions { Strategy = LayoutStrategy.Layered });

        var page = diagram.Pages[0];
        var before = page.Nodes.Select(n => (n.Bounds.X, n.Bounds.Y, n.Bounds.W, n.Bounds.H)).ToList();
        var sizeBefore = (page.Size!.Width, page.Size.Height);

        DiagramLayout.Transpose(diagram);
        DiagramLayout.Transpose(diagram);

        var after = page.Nodes.Select(n => (n.Bounds.X, n.Bounds.Y, n.Bounds.W, n.Bounds.H)).ToList();

        for (int i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].X, after[i].X, 3);
            Assert.Equal(before[i].Y, after[i].Y, 3);
            Assert.Equal(before[i].W, after[i].W, 3);
            Assert.Equal(before[i].H, after[i].H, 3);
        }

        Assert.Equal(sizeBefore.Width, page.Size.Width, 3);
        Assert.Equal(sizeBefore.Height, page.Size.Height, 3);
    }

    [Fact]
    public void Transpose_MapsAngleToNinetyMinusTheta()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 10 },
            Nodes = { new Node { Id = "r", Text = "R", Bounds = new Bounds(5, 5, 3, 0.2), Angle = 30 } }
        };
        var diagram = new Diagram { Pages = { page } };

        DiagramLayout.Transpose(diagram);

        // A bar lying at 30 degrees must end up at 60, not at 30 and not at -30.
        Assert.Equal(60, page.Nodes[0].Angle!.Value, 3);
    }

    [Fact]
    public void Transpose_MirrorsDecorationsAndGeometryTogether()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 6 },
            TextBlocks = { new TextBlock { Id = "t", Text = "T", Bounds = new Bounds(2, 5, 4, 0.5) } },
            Paths =
            {
                new FreePath
                {
                    Id = "bar",
                    Bounds = new Bounds(7, 2, 3, 0.2),
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
        var diagram = new Diagram { Pages = { page } };

        DiagramLayout.Transpose(diagram);

        Assert.Equal(5, page.TextBlocks[0].Bounds.X, 3);
        Assert.Equal(2, page.TextBlocks[0].Bounds.Y, 3);
        Assert.Equal(2, page.Paths[0].Bounds.X, 3);
        Assert.Equal(7, page.Paths[0].Bounds.Y, 3);

        // The outline has to move with its box, or the bar no longer matches its own bounds.
        var move = page.Paths[0].Geometry[0].Rows[0].Cells;
        Assert.Equal("1", move["X"]);
        Assert.Equal("0", move["Y"]);
    }

    [Fact]
    public void Transpose_SwapsPageSizeAndOrientation()
    {
        var diagram = new Diagram
        {
            Orientation = "landscape",
            PageSize = new PageSize { Width = 14, Height = 9 },
            Pages =
            {
                new Page { Name = "p", Size = new PageSize { Width = 14, Height = 9 } }
            }
        };

        DiagramLayout.Transpose(diagram);

        var pageSize = diagram.Pages[0].Size;
        Assert.NotNull(pageSize);
        Assert.Equal(9, pageSize.Width, 3);
        Assert.Equal(14, pageSize.Height, 3);
        Assert.Equal(9, diagram.PageSize!.Width, 3);
        Assert.Equal("portrait", diagram.Orientation);
    }
}
