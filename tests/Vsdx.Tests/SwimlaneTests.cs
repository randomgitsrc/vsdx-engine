using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// SwimlaneTests: cross-functional swimlanes, the fourth of the ten required diagram categories.
///
/// Phase 7 gap G4 found `Group.Kind = "lane"` was accepted by the model but read by nothing, so
/// this diagram type could not be produced at all. Lanes stack vertically while their contents
/// flow horizontally — reusing the banded machinery rather than a second layout system.
/// </summary>
public sealed class SwimlaneTests
{
    private static Diagram Lanes()
    {
        var page = new Page
        {
            Name = "swim",
            Size = new PageSize { Width = 16, Height = 11 },
            Groups =
            {
                new Group { Id = "sales", Name = "销售部", Kind = "lane", Members = { "s1", "s2", "s3" } },
                new Group { Id = "fin", Name = "财务部", Kind = "lane", Members = { "f1", "f2" } },
                new Group { Id = "ops", Name = "运营部", Kind = "lane", Members = { "o1", "o2" } }
            },
            Nodes =
            {
                // Author order deliberately scrambled to prove ordering comes from the graph.
                new Node { Id = "s2", Text = "报价", Bounds = new Bounds(5, 8, 2, 0.9), GroupId = "sales" },
                new Node { Id = "s1", Text = "接单", Bounds = new Bounds(2, 8, 2, 0.9), GroupId = "sales" },
                new Node { Id = "s3", Text = "签约", Bounds = new Bounds(8, 8, 2, 0.9), GroupId = "sales" },
                new Node { Id = "f1", Text = "审核", Bounds = new Bounds(5, 5, 2, 0.9), GroupId = "fin" },
                new Node { Id = "f2", Text = "开票", Bounds = new Bounds(8, 5, 2, 0.9), GroupId = "fin" },
                new Node { Id = "o1", Text = "备货", Bounds = new Bounds(2, 2, 2, 0.9), GroupId = "ops" },
                new Node { Id = "o2", Text = "发货", Bounds = new Bounds(5, 2, 2, 0.9), GroupId = "ops" }
            },
            Edges =
            {
                new Edge { Id = "e1", From = "s1", To = "s2" },
                new Edge { Id = "e2", From = "s2", To = "s3" },
                new Edge { Id = "e3", From = "s3", To = "f1" },
                new Edge { Id = "e4", From = "f1", To = "f2" },
                new Edge { Id = "e5", From = "f2", To = "o1" },
                new Edge { Id = "e6", From = "o1", To = "o2" }
            }
        };

        return new Diagram { Type = "workflow", PageSize = page.Size, Pages = { page } };
    }

    [Fact]
    public void Swimlane_IsSelectedWhenLanesAreDeclared()
    {
        Assert.Equal(LayoutStrategy.Swimlane, DiagramLayoutEngine.SuggestStrategy(Lanes()));
    }

    [Fact]
    public void Swimlane_ParseAcceptsNames()
    {
        Assert.Equal(LayoutStrategy.Swimlane, DiagramLayoutEngine.Parse("swimlane"));
        Assert.Equal(LayoutStrategy.Swimlane, DiagramLayoutEngine.Parse("lane"));
        Assert.Equal(LayoutStrategy.Swimlane, DiagramLayoutEngine.Parse("lanes"));
    }

    [Fact]
    public void Swimlane_StacksLanesVertically()
    {
        var diagram = Lanes();
        new SwimlaneLayoutEngine().Layout(diagram.Pages[0], new LayoutOptions());

        var page = diagram.Pages[0];
        double Y(string id) => page.Nodes.Single(n => n.Id == id).Bounds.Y;

        // Every node in a lane shares one band, and bands descend in the declared order.
        Assert.Equal(Y("s1"), Y("s2"), 3);
        Assert.True(Y("s1") > Y("f1"), "sales lane must sit above finance");
        Assert.True(Y("f1") > Y("o1"), "finance lane must sit above operations");
    }

    [Fact]
    public void Swimlane_FlowsLeftToRightInsideEachLane()
    {
        var diagram = Lanes();
        new SwimlaneLayoutEngine().Layout(diagram.Pages[0], new LayoutOptions());

        var page = diagram.Pages[0];
        double X(string id) => page.Nodes.Single(n => n.Id == id).Bounds.X;

        Assert.True(X("s1") < X("s2"));
        Assert.True(X("s2") < X("s3"));
        Assert.True(X("f1") < X("f2"));
        Assert.True(X("o1") < X("o2"));
    }

    [Fact]
    public void Swimlane_KeepsLanesApart()
    {
        var diagram = Lanes();
        new SwimlaneLayoutEngine().Layout(diagram.Pages[0], new LayoutOptions());
        DiagramLayout.Apply(diagram);

        var groups = diagram.Pages[0].Groups.ToDictionary(g => g.Id, g => g.Bounds!);

        double Overlap(string a, string b) =>
            Math.Min(groups[a].Y + groups[a].H / 2, groups[b].Y + groups[b].H / 2)
          - Math.Max(groups[a].Y - groups[a].H / 2, groups[b].Y - groups[b].H / 2);

        Assert.True(Overlap("sales", "fin") < 0, "sales and finance lanes must not intersect");
        Assert.True(Overlap("fin", "ops") < 0, "finance and operations lanes must not intersect");
    }

    [Fact]
    public void Swimlane_PlacesEveryNode()
    {
        var diagram = Lanes();
        var result = new SwimlaneLayoutEngine().Layout(diagram.Pages[0], new LayoutOptions());

        Assert.Equal(7, result.NodesPlaced);
    }

    [Fact]
    public void Swimlane_IsDeterministic()
    {
        var a = Lanes();
        var b = Lanes();
        new SwimlaneLayoutEngine().Layout(a.Pages[0], new LayoutOptions());
        new SwimlaneLayoutEngine().Layout(b.Pages[0], new LayoutOptions());

        Assert.Equal(
            a.Pages[0].Nodes.OrderBy(n => n.Id).Select(n => (n.Id, n.Bounds.X, n.Bounds.Y)),
            b.Pages[0].Nodes.OrderBy(n => n.Id).Select(n => (n.Id, n.Bounds.X, n.Bounds.Y)));
    }

    [Fact]
    public void Swimlane_WithoutLanesFallsBackToBanded()
    {
        // A page with ordinary containers must not be forced through swimlane logic.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 9 },
            Groups = { new Group { Id = "g", Name = "Band", Members = { "a", "b" } } },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 6, 2, 0.9), GroupId = "g" },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(7, 6, 2, 0.9), GroupId = "g" }
            }
        };

        var result = new SwimlaneLayoutEngine().Layout(page, new LayoutOptions());

        Assert.Equal(2, result.NodesPlaced);
    }

    [Fact]
    public void Swimlane_LooseNodesGoAboveLanes()
    {
        var diagram = Lanes();
        diagram.Pages[0].Nodes.Add(new Node
        {
            Id = "free", Text = "外部", Bounds = new Bounds(3, 10, 2, 0.9)
        });

        new SwimlaneLayoutEngine().Layout(diagram.Pages[0], new LayoutOptions());

        var page = diagram.Pages[0];
        double freeY = page.Nodes.Single(n => n.Id == "free").Bounds.Y;
        double salesY = page.Nodes.Single(n => n.Id == "s1").Bounds.Y;

        Assert.True(freeY > salesY, "unassigned nodes belong above the lanes");
    }

    [Fact]
    public void Swimlane_ProducesDeliverableDiagram()
    {
        var dir = TestData.NewTempDir("swimlane");
        var vsdx = Path.Combine(dir, "swim.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Lanes(), vsdx, new WriteOptions
        {
            Layout = "auto", FitToText = true, FitPage = true, Recenter = true
        });

        var report = LayoutAnalyzer.Analyze(engine.Read(vsdx).Diagram);
        Assert.True(report.HardConstraintsPassed,
            string.Join("; ", report.Errors.Select(e => e.Code)));
    }

    [Fact]
    public void Swimlane_SurvivesRoundTrip()
    {
        var dir = TestData.NewTempDir("swimlane-rt");
        var vsdx = Path.Combine(dir, "swim.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Lanes(), vsdx, new WriteOptions { Layout = "auto", FitPage = true });

        var page = engine.Read(vsdx).Diagram.Pages[0];
        Assert.Equal(3, page.Groups.Count(g => string.Equals(g.Kind, "container", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(7, page.Nodes.Count);
    }
}
