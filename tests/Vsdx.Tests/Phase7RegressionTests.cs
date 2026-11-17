using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Phase7RegressionTests: locks the defects found by the Phase 7 audit.
///
/// Each test corresponds to a numbered gap in docs/phase7/phase0-6-implementation-audit.md and
/// fails against the pre-audit code. They exist so the same regressions cannot return silently.
/// </summary>
public sealed class Phase7RegressionTests
{
    // ---------------------------------------------------------------------
    // G1: nested containers must survive a save/read cycle.
    // Before the fix: ParentId was never written, never read, and the child's
    // members vanished from the parent.
    // ---------------------------------------------------------------------
    private static Diagram Nested()
    {
        return new Diagram
        {
            Id = "nested",
            Name = "Nested",
            Type = "architecture",
            PageSize = new PageSize { Width = 16, Height = 11 },
            Pages =
            {
                new Page
                {
                    Name = "N",
                    Size = new PageSize { Width = 16, Height = 11 },
                    Groups =
                    {
                        new Group { Id = "outer", Name = "Outer Zone", Kind = "container", AutoSize = true },
                        new Group
                        {
                            Id = "inner", Name = "Inner Cluster", Kind = "container",
                            ParentId = "outer", AutoSize = true
                        }
                    },
                    Nodes =
                    {
                        new Node { Id = "a", Text = "Node A", Bounds = new Bounds(3, 8, 2, 0.9), GroupId = "inner" },
                        new Node { Id = "b", Text = "Node B", Bounds = new Bounds(6, 8, 2, 0.9), GroupId = "inner" },
                        new Node { Id = "c", Text = "Outside", Bounds = new Bounds(10, 8, 2, 0.9), GroupId = "outer" }
                    },
                    Edges = { new Edge { Id = "e1", From = "a", To = "c", Kind = "straight" } }
                }
            }
        };
    }

    [Fact]
    public void G1_NestedContainerSurvivesRoundTrip()
    {
        var dir = TestData.NewTempDir("p7-nested");
        var vsdx = Path.Combine(dir, "nested.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Nested(), vsdx);

        var page = engine.Read(vsdx).Diagram.Pages[0];
        var inner = page.Groups.Single(g => g.Id == "inner");

        Assert.Equal("outer", inner.ParentId);
    }

    [Fact]
    public void G1_NestedContainerWritesParentCell()
    {
        var dir = TestData.NewTempDir("p7-nested-xml");
        var vsdx = Path.Combine(dir, "nested.vsdx");

        new VsdxEngine().Create(Nested(), vsdx);

        using var zip = System.IO.Compression.ZipFile.OpenRead(vsdx);
        using var reader = new StreamReader(zip.GetEntry("visio/pages/page1.xml")!.Open());
        var xml = reader.ReadToEnd();

        // Visio records nesting as a Parent cell; without it the shapes are siblings.
        Assert.Contains("N=\"Parent\"", xml);
    }

    [Fact]
    public void G1_ParentContainerIncludesNestedMembers()
    {
        var dir = TestData.NewTempDir("p7-nested-members");
        var vsdx = Path.Combine(dir, "nested.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Nested(), vsdx);

        var page = engine.Read(vsdx).Diagram.Pages[0];
        var outer = page.Groups.Single(g => g.Id == "outer");

        // The inner container's members belong to the outer zone transitively.
        Assert.Contains("a", outer.Members);
        Assert.Contains("b", outer.Members);
        Assert.Contains("c", outer.Members);
    }

    // ---------------------------------------------------------------------
    // G2: the create pipeline must fit the page AFTER placement.
    // Before the fix: FitPage ran on the author's stale coordinates, then
    // layout moved everything, leaving a large empty band. Measured: 7.09"
    // of wasted width on a machine-scored 90/100 diagram.
    // ---------------------------------------------------------------------
    [Fact]
    public void G2_PageIsFittedAfterPlacement()
    {
        var dir = TestData.NewTempDir("p7-order");
        var vsdx = Path.Combine(dir, "out.vsdx");

        // Author coordinates deliberately clustered in one corner.
        var model = new Diagram
        {
            Id = "order", Name = "Order", Type = "architecture",
            PageSize = new PageSize { Width = 22, Height = 9 },
            Pages =
            {
                new Page
                {
                    Name = "P", Size = new PageSize { Width = 22, Height = 9 },
                    Nodes =
                    {
                        new Node { Id = "a", Text = "Alpha", Bounds = new Bounds(2, 2, 2, 0.9) },
                        new Node { Id = "b", Text = "Beta", Bounds = new Bounds(5, 2, 2, 0.9) },
                        new Node { Id = "c", Text = "Gamma", Bounds = new Bounds(8, 2, 2, 0.9) }
                    },
                    Edges =
                    {
                        new Edge { Id = "e1", From = "a", To = "b" },
                        new Edge { Id = "e2", From = "b", To = "c" }
                    }
                }
            }
        };

        var engine = new VsdxEngine();
        engine.Create(model, vsdx, new WriteOptions
        {
            Layout = "auto", FitToText = true, FitPage = true, Recenter = true
        });

        var page = engine.Read(vsdx).Diagram.Pages[0];

        // A three-node chain lays out as a vertical stack, so the page is *meant* to be narrow.
        // The defect being guarded against is not "narrow content", it is "page much larger than
        // content". Assert on the utilisation ratio and on the absolute overhang.
        double maxX = page.Nodes.Max(n => n.Bounds.X + n.Bounds.W / 2);
        double wasted = page.Size!.Width - maxX;

        Assert.True(wasted < 2.5,
            $"page {page.Size.Width:F2} leaves {wasted:F2}\" empty to the right of content at {maxX:F2}");

        var report = LayoutAnalyzer.Analyze(engine.Read(vsdx).Diagram);
        Assert.False(report.PageUtilisation.Values.All(v => v < 0.15),
            "an almost-empty page is the defect this guards against");
    }

    [Fact]
    public void G2_PlacementHappensBeforePageFit()
    {
        var dir = TestData.NewTempDir("p7-order2");
        var vsdx = Path.Combine(dir, "out.vsdx");

        var model = TestData.Architecture15();
        model.PageSize = new PageSize { Width = 30, Height = 14 };
        model.Pages[0].Size = new PageSize { Width = 30, Height = 14 };

        var engine = new VsdxEngine();
        engine.Create(model, vsdx, new WriteOptions
        {
            Layout = "layered", FitToText = true, FitPage = true, Recenter = true
        });

        var page = engine.Read(vsdx).Diagram.Pages[0];
        double maxX = page.Nodes.Max(n => n.Bounds.X + n.Bounds.W / 2);

        Assert.True(page.Size!.Width - maxX < 2.0,
            "a very wide requested page must shrink to the laid-out content");
    }

    // ---------------------------------------------------------------------
    // G2b: layered ordering must follow the graph, not predecessor X.
    // Before the fix: sources scored double.MaxValue and were placed AFTER
    // their own successors, so every simple chain read backwards.
    // ---------------------------------------------------------------------
    [Fact]
    public void G2b_ChainReadsLeftToRight()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Groups = { new Group { Id = "g", Name = "Band", Members = { "s1", "s2" } } },
            Nodes =
            {
                // Author order deliberately reversed.
                new Node { Id = "s2", Text = "Second", Bounds = new Bounds(3, 6, 2, 0.8), GroupId = "g" },
                new Node { Id = "s1", Text = "First", Bounds = new Bounds(7, 6, 2, 0.8), GroupId = "g" }
            },
            Edges = { new Edge { Id = "e", From = "s1", To = "s2" } }
        };

        new LayeredLayoutEngine().Layout(page, new LayoutOptions());

        var first = page.Nodes.Single(n => n.Id == "s1");
        var second = page.Nodes.Single(n => n.Id == "s2");

        Assert.True(first.Bounds.X < second.Bounds.X,
            "a source node must be placed before the node it feeds");
    }

    [Fact]
    public void G2b_OrderingIsIndependentOfAuthorX()
    {
        // Two runs with different author X values must produce the same left-to-right order.
        static Page Build(double x1, double x2) => new()
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Groups = { new Group { Id = "g", Name = "Band", Members = { "a", "b", "c" } } },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(x1, 6, 2, 0.8), GroupId = "g" },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(x2, 6, 2, 0.8), GroupId = "g" },
                new Node { Id = "c", Text = "C", Bounds = new Bounds(x1, 3, 2, 0.8), GroupId = "g" }
            },
            Edges =
            {
                new Edge { Id = "ab", From = "a", To = "b" },
                new Edge { Id = "bc", From = "b", To = "c" }
            }
        };

        var first = Build(2, 9);
        var second = Build(9, 2);

        new LayeredLayoutEngine().Layout(first, new LayoutOptions());
        new LayeredLayoutEngine().Layout(second, new LayoutOptions());

        static string Order(Page p) =>
            string.Join(",", p.Nodes.OrderBy(n => n.Bounds.X).Select(n => n.Id));

        Assert.Equal(Order(first), Order(second));
    }

    // ---------------------------------------------------------------------
    // G8: an almost-empty page must not pass as deliverable.
    // The audit found `page.underused` was `info`, so a diagram using 21%
    // of its page still scored 90 and was called "deliverable".
    // ---------------------------------------------------------------------
    [Fact]
    public void G8_SeverelyUnderusedPageIsReported()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 30, Height = 20 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 3, 2, 0.9) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 3, 2, 0.9) }
            }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.Contains(report.Issues, i => i.Code == "page.underused");
        Assert.True(report.PageUtilisation["p"] < 0.4);
    }

    [Fact]
    public void G8_DeliveredPipelineDoesNotLeaveHugeGap()
    {
        // End-to-end version of the same check: after the fixed pipeline, every benchmark's
        // content must occupy a reasonable share of its page.
        var dir = TestData.NewTempDir("p7-util");
        var engine = new VsdxEngine();
        var vsdx = Path.Combine(dir, "out.vsdx");

        engine.Create(TestData.Architecture15(), vsdx, new WriteOptions
        {
            Layout = "auto", FitToText = true, FitPage = true, Recenter = true
        });

        var report = LayoutAnalyzer.Analyze(engine.Read(vsdx).Diagram);
        Assert.True(report.PageUtilisation.Values.Max() >= 0.30,
            "content should occupy a meaningful share of the page after fitting");
    }
}
