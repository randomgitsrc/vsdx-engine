using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// LayoutTests: node placement, which the engine previously did not do at all.
///
/// Phase 0 established that `AutoLayout` only derived container bounds and routed connectors;
/// every node coordinate came from hand-written JSON. These tests cover the placement engines
/// that close that gap.
/// </summary>
public sealed class LayoutTests
{
    private static Page ThreeNodePage() => new()
    {
        Name = "p",
        Size = new PageSize { Width = 14, Height = 9 },
        Nodes =
        {
            new Node { Id = "c", Text = "C", Bounds = new Bounds(3, 3, 1.6, 0.8) },
            new Node { Id = "a", Text = "A", Bounds = new Bounds(9, 7, 1.6, 0.8) },
            new Node { Id = "b", Text = "B", Bounds = new Bounds(5, 5, 1.6, 0.8) }
        },
        Edges =
        {
            new Edge { Id = "ab", From = "a", To = "b" },
            new Edge { Id = "bc", From = "b", To = "c" }
        }
    };

    [Fact]
    public void Layered_PlacesEveryNode()
    {
        var page = ThreeNodePage();
        var result = new LayeredLayoutEngine().Layout(page, new LayoutOptions());

        Assert.Equal(3, result.NodesPlaced);
        // No two nodes share a position.
        var positions = page.Nodes.Select(n => (n.Bounds.X, n.Bounds.Y)).ToList();
        Assert.Equal(positions.Count, positions.Distinct().Count());
    }

    [Fact]
    public void Layered_KeepsNodesInsidePage()
    {
        var page = ThreeNodePage();
        new LayeredLayoutEngine().Layout(page, new LayoutOptions());

        foreach (var node in page.Nodes)
        {
            Assert.True(node.Bounds.X - node.Bounds.W / 2 >= -0.01);
            Assert.True(node.Bounds.Y - node.Bounds.H / 2 >= -0.01);
            Assert.True(node.Bounds.X + node.Bounds.W / 2 <= page.Size!.Width + 0.01);
            Assert.True(node.Bounds.Y + node.Bounds.H / 2 <= page.Size.Height + 0.01);
        }
    }

    [Fact]
    public void Layered_IsDeterministic()
    {
        var first = ThreeNodePage();
        var second = ThreeNodePage();

        var a = new LayeredLayoutEngine().Layout(first, new LayoutOptions());
        var b = new LayeredLayoutEngine().Layout(second, new LayoutOptions());

        Assert.Equal(
            first.Nodes.Select(n => (n.Id, n.Bounds.X, n.Bounds.Y)).OrderBy(t => t.Id),
            second.Nodes.Select(n => (n.Id, n.Bounds.X, n.Bounds.Y)).OrderBy(t => t.Id));
        Assert.Equal(a.Layers, b.Layers);
    }

    [Fact]
    public void Layered_RanksByEdgeDirection()
    {
        // a -> b -> c: c must end up on the lowest band, a on the highest.
        var page = ThreeNodePage();
        new LayeredLayoutEngine().Layout(page, new LayoutOptions());

        var a = page.Nodes.Single(n => n.Id == "a");
        var b = page.Nodes.Single(n => n.Id == "b");
        var c = page.Nodes.Single(n => n.Id == "c");

        Assert.True(a.Bounds.Y > b.Bounds.Y);
        Assert.True(b.Bounds.Y > c.Bounds.Y);
    }

    [Fact]
    public void Layered_KeepsContainersApart()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 12 },
            Groups =
            {
                new Group { Id = "top", Name = "Top", Members = { "a", "b" } },
                new Group { Id = "bottom", Name = "Bottom", Members = { "c" } }
            },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 9, 1.8, 0.8), GroupId = "top" },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(6, 9, 1.8, 0.8), GroupId = "top" },
                new Node { Id = "c", Text = "C", Bounds = new Bounds(3, 4, 1.8, 0.8), GroupId = "bottom" }
            }
        };

        new LayeredLayoutEngine().Layout(page, new LayoutOptions());

        var top = page.Groups.Single(g => g.Id == "top").Bounds!;
        var bottom = page.Groups.Single(g => g.Id == "bottom").Bounds!;
        double overlap = Math.Min(top.Y + top.H / 2, bottom.Y + bottom.H / 2)
                       - Math.Max(top.Y - top.H / 2, bottom.Y - bottom.H / 2);
        Assert.True(overlap < 0, $"container bands must not intersect, overlap was {overlap:F2}");
    }

    [Fact]
    public void Layered_PreservesContainerLayerOrder()
    {
        // The container whose members sit highest must remain the top band.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 12 },
            Groups =
            {
                new Group { Id = "upper", Members = { "u" } },
                new Group { Id = "lower", Members = { "l" } }
            },
            Nodes =
            {
                new Node { Id = "u", Text = "U", Bounds = new Bounds(3, 9, 1.8, 0.8), GroupId = "upper" },
                new Node { Id = "l", Text = "L", Bounds = new Bounds(3, 3, 1.8, 0.8), GroupId = "lower" }
            }
        };

        new LayeredLayoutEngine().Layout(page, new LayoutOptions());

        var upperY = page.Nodes.Single(n => n.Id == "u").Bounds.Y;
        var lowerY = page.Nodes.Single(n => n.Id == "l").Bounds.Y;
        Assert.True(upperY > lowerY, "the band the author placed on top must stay on top");
    }

    [Fact]
    public void Flow_OrdersAlongEdges()
    {
        var page = ThreeNodePage();
        new FlowLayoutEngine().Layout(page, new LayoutOptions());

        var a = page.Nodes.Single(n => n.Id == "a");
        var b = page.Nodes.Single(n => n.Id == "b");
        var c = page.Nodes.Single(n => n.Id == "c");

        Assert.True(a.Bounds.X < b.Bounds.X);
        Assert.True(b.Bounds.X < c.Bounds.X);
    }

    [Fact]
    public void Flow_HandlesCyclesWithoutDroppingNodes()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 5, 1.6, 0.8) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(5, 5, 1.6, 0.8) },
                new Node { Id = "c", Text = "C", Bounds = new Bounds(8, 5, 1.6, 0.8) }
            },
            Edges =
            {
                new Edge { Id = "ab", From = "a", To = "b" },
                new Edge { Id = "bc", From = "b", To = "c" },
                new Edge { Id = "ca", From = "c", To = "a" }   // cycle
            }
        };

        var result = new FlowLayoutEngine().Layout(page, new LayoutOptions());

        Assert.Equal(3, result.NodesPlaced);
        Assert.All(page.Nodes, n => Assert.True(n.Bounds.X > 0));
    }

    [Fact]
    public void Grid_PlacesInRowsAndColumns()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Nodes = Enumerable.Range(0, 6)
                .Select(i => new Node { Id = $"n{i}", Text = $"N{i}", Bounds = new Bounds(1, 1, 1.6, 0.8) })
                .ToList()
        };

        new GridLayoutEngine().Layout(page, new LayoutOptions { MaxNodesPerRow = 3 });

        var rows = page.Nodes.GroupBy(n => Math.Round(n.Bounds.Y, 2)).Count();
        Assert.Equal(2, rows);
    }

    [Fact]
    public void Tree_PutsRootOnTop()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 10 },
            Nodes =
            {
                new Node { Id = "ceo", Text = "CEO", Bounds = new Bounds(5, 3, 1.8, 0.9) },
                new Node { Id = "cto", Text = "CTO", Bounds = new Bounds(3, 5, 1.8, 0.9) },
                new Node { Id = "cfo", Text = "CFO", Bounds = new Bounds(7, 5, 1.8, 0.9) }
            },
            Edges =
            {
                new Edge { Id = "1", From = "ceo", To = "cto" },
                new Edge { Id = "2", From = "ceo", To = "cfo" }
            }
        };

        new TreeLayoutEngine().Layout(page, new LayoutOptions());

        var ceo = page.Nodes.Single(n => n.Id == "ceo");
        Assert.True(ceo.Bounds.Y > page.Nodes.Single(n => n.Id == "cto").Bounds.Y);
        Assert.True(ceo.Bounds.Y > page.Nodes.Single(n => n.Id == "cfo").Bounds.Y);
    }

    [Fact]
    public void Manual_LeavesCoordinatesUntouched()
    {
        var page = ThreeNodePage();
        var before = page.Nodes.Select(n => (n.Id, n.Bounds.X, n.Bounds.Y)).OrderBy(t => t.Id).ToList();

        new ManualLayoutEngine().Layout(page, new LayoutOptions());

        var after = page.Nodes.Select(n => (n.Id, n.Bounds.X, n.Bounds.Y)).OrderBy(t => t.Id).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Auto_SelectsStrategyFromDiagramType()
    {
        var architecture = new Diagram { Type = "architecture", Pages = { ThreeNodePage() } };
        Assert.Equal(LayoutStrategy.Layered, DiagramLayoutEngine.SuggestStrategy(architecture));

        var org = new Diagram { Type = "org", Pages = { ThreeNodePage() } };
        Assert.Equal(LayoutStrategy.Tree, DiagramLayoutEngine.SuggestStrategy(org));

        // A workflow without containers is a plain pipeline and reads along its edges. Bands read
        // better only when there are containers to band — which the Layered assertions above cover.
        var flow = new Diagram { Type = "workflow", Pages = { ThreeNodePage() } };
        Assert.Equal(LayoutStrategy.Flow, DiagramLayoutEngine.SuggestStrategy(flow));
    }

    [Fact]
    public void Auto_WorkflowWithoutDecisionsUsesFlow()
    {
        // A workflow without containers reads along its edges, with or without a decision diamond.
        // Requiring one sent a pure data flow to Layered, where its eight-node chain became eight
        // layers: measured at 23.3" x 7.5", a 3.1:1 strip, against 8.5" x 15.6" from Flow — which
        // also scored higher. Layering remains right as soon as containers are present.
        var page = ThreeNodePage();   // no Diamond nodes
        var flow = new Diagram { Type = "workflow", Pages = { page } };

        Assert.Equal(LayoutStrategy.Flow, DiagramLayoutEngine.SuggestStrategy(flow));

        page.Nodes.Add(new Node
        {
            Id = "d", Text = "?", Shape = "Diamond", Bounds = new Bounds(5, 2, 1.4, 1.0)
        });

        Assert.Equal(LayoutStrategy.Flow, DiagramLayoutEngine.SuggestStrategy(flow));
    }

    [Fact]
    public void Parse_AcceptsKnownNames()
    {
        Assert.Equal(LayoutStrategy.Layered, DiagramLayoutEngine.Parse("layered"));
        Assert.Equal(LayoutStrategy.Grid, DiagramLayoutEngine.Parse("GRID"));
        Assert.Equal(LayoutStrategy.Flow, DiagramLayoutEngine.Parse(" pipeline "));
        Assert.Equal(LayoutStrategy.Tree, DiagramLayoutEngine.Parse("orgchart"));
        Assert.Equal(LayoutStrategy.Manual, DiagramLayoutEngine.Parse("none"));
        Assert.Equal(LayoutStrategy.Layered, DiagramLayoutEngine.Parse(null));
    }

    [Fact]
    public void Relayout_DropsStaleWaypoints()
    {
        // Waypoints are absolute; after nodes move they describe a route through empty space.
        var page = ThreeNodePage();
        page.Edges[0].Waypoints = new List<Point> { new(1, 1), new(13, 1) };

        DiagramLayoutEngine.Apply(new Diagram { Pages = { page } }, null, LayoutStrategy.Layered);

        Assert.True(page.Edges[0].Waypoints is null || page.Edges[0].Waypoints!.Count == 0);
    }

    [Fact]
    public void Engine_PlacesNodesEndToEnd()
    {
        var dir = TestData.NewTempDir("layout-engine");
        var path = Path.Combine(dir, "auto.vsdx");

        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path, new WriteOptions
        {
            Layout = "layered",
            FitToText = true,
            FitPage = true,
            Recenter = true
        });

        var page = engine.Read(path).Diagram.Pages[0];
        Assert.Equal(3, page.Nodes.Count);

        var report = LayoutAnalyzer.Analyze(engine.Read(path).Diagram);
        Assert.True(report.HardConstraintsPassed,
            string.Join("; ", report.Errors.Select(e => e.Code)));
    }
}
