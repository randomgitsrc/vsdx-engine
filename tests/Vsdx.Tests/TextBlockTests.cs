using Vsdx.Adapters;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// TextBlockTests: standalone text (title, subtitle, legend, note) must exist as its own object.
///
/// These are not nodes: they take no part in layout, have no edges, and are positioned against the
/// page. Expressing them as zero-edge nodes would corrupt layout and quality metrics, so they are
/// a separate collection — and Phase 8 found the engine had no such thing at all.
/// </summary>
public sealed class TextBlockTests
{
    private static Diagram WithText() => new()
    {
        Id = "tb", Name = "Text demo", Type = "architecture",
        PageSize = new PageSize { Width = 11, Height = 8 },
        Pages =
        {
            new Page
            {
                Name = "P", Size = new PageSize { Width = 11, Height = 8 },
                Nodes =
                {
                    new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 4, 2, 0.9) },
                    new Node { Id = "b", Text = "B", Bounds = new Bounds(7, 4, 2, 0.9) }
                },
                Edges = { new Edge { Id = "e1", From = "a", To = "b" } },
                TextBlocks =
                {
                    new TextBlock
                    {
                        Id = "title", Text = "订单处理平台", Role = "title",
                        Bounds = new Bounds(5.5, 7.3, 6, 0.6), Align = "center",
                        Style = new ShapeStyle { Bold = true }
                    },
                    new TextBlock
                    {
                        Id = "subtitle", Text = "Order Processing Platform", Role = "subtitle",
                        Bounds = new Bounds(5.5, 6.8, 6, 0.4), Align = "center"
                    },
                    new TextBlock
                    {
                        Id = "legend", Text = "蓝 = 服务", Role = "legend",
                        Bounds = new Bounds(5.5, 1.4, 6, 0.35), Align = "center"
                    },
                    new TextBlock
                    {
                        Id = "note", Text = "注：每日同步", Role = "note",
                        Bounds = new Bounds(2.6, 0.8, 3, 0.3), Align = "left",
                        Style = new ShapeStyle { Italic = true }
                    }
                }
            }
        }
    };

    [Fact]
    public void TextBlocksAreWritten()
    {
        var dir = TestData.NewTempDir("tb-write");
        var path = Path.Combine(dir, "t.vsdx");

        new VsdxEngine().Create(WithText(), path);

        var doc = FidelityTests.ReadPage(path);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
        int textShapes = doc.Descendants(ns + "Shape")
            .Count(s => (string?)s.Attribute("NameU") == "TextBlock");

        Assert.Equal(4, textShapes);
    }

    [Fact]
    public void TextBlocksReadBackSeparateFromNodes()
    {
        var dir = TestData.NewTempDir("tb-read");
        var path = Path.Combine(dir, "t.vsdx");

        var engine = new VsdxEngine();
        engine.Create(WithText(), path);
        var page = engine.Read(path).Diagram.Pages[0];

        // Critically: the title must NOT appear as a node.
        Assert.Equal(2, page.Nodes.Count);
        Assert.Equal(4, page.TextBlocks.Count);
        Assert.DoesNotContain(page.Nodes, n => n.Id == "title");
    }

    [Fact]
    public void TextContentSurvivesRoundTrip()
    {
        var dir = TestData.NewTempDir("tb-content");
        var path = Path.Combine(dir, "t.vsdx");

        var engine = new VsdxEngine();
        engine.Create(WithText(), path);
        var page = engine.Read(path).Diagram.Pages[0];

        var title = page.TextBlocks.Single(t => t.Id == "title");
        Assert.Equal("订单处理平台", title.Text);
    }

    [Fact]
    public void RolesSurviveRoundTrip()
    {
        var dir = TestData.NewTempDir("tb-roles");
        var path = Path.Combine(dir, "t.vsdx");

        var engine = new VsdxEngine();
        engine.Create(WithText(), path);
        var page = engine.Read(path).Diagram.Pages[0];

        Assert.Equal("title", page.TextBlocks.Single(t => t.Id == "title").Role);
        Assert.Equal("subtitle", page.TextBlocks.Single(t => t.Id == "subtitle").Role);
        Assert.Equal("legend", page.TextBlocks.Single(t => t.Id == "legend").Role);
        Assert.Equal("note", page.TextBlocks.Single(t => t.Id == "note").Role);
    }

    [Fact]
    public void RoleDrivesTypeSize()
    {
        // A title must be visibly larger than a note; otherwise the text is not doing its job.
        Assert.True(VisioTextBlockBridge.SizeForRole("title") >
                    VisioTextBlockBridge.SizeForRole("subtitle"));
        Assert.True(VisioTextBlockBridge.SizeForRole("subtitle") >
                    VisioTextBlockBridge.SizeForRole("note"));
    }

    [Fact]
    public void FontSizeBelowReadableMinimumIsClamped()
    {
        var dir = TestData.NewTempDir("tb-min");
        var diagram = WithText();
        diagram.Pages[0].TextBlocks[3].Style = new ShapeStyle { FontSize = 3 };

        var path = Path.Combine(dir, "t.vsdx");
        new VsdxEngine().Create(diagram, path);

        var doc = FidelityTests.ReadPage(path);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
        var sizeCell = doc.Descendants(ns + "Cell")
            .FirstOrDefault(c => (string?)c.Attribute("N") == "Size"
                                 && ((string?)c.Attribute("V")) is not null);

        Assert.NotNull(sizeCell);
        // Stored in inches; 8pt = 0.111... in.
        double inches = double.Parse((string)sizeCell!.Attribute("V")!,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(inches >= 8.0 / 72.0 - 1e-6,
            $"font size {inches * 72:F1}pt is below the readable floor");
    }

    [Fact]
    public void TextBlocksDoNotAffectLayout()
    {
        // Adding a title must not move the nodes: text is not part of the layout graph.
        var dir = TestData.NewTempDir("tb-layout");

        var without = WithText();
        without.Pages[0].TextBlocks.Clear();
        var a = Path.Combine(dir, "a.vsdx");
        new VsdxEngine().Create(without, a, new WriteOptions { Layout = "manual" });

        var b = Path.Combine(dir, "b.vsdx");
        new VsdxEngine().Create(WithText(), b, new WriteOptions { Layout = "manual" });

        var engine = new VsdxEngine();
        var nodesA = engine.Read(a).Diagram.Pages[0].Nodes.OrderBy(n => n.Id)
            .Select(n => (n.Id, n.Bounds.X, n.Bounds.Y)).ToList();
        var nodesB = engine.Read(b).Diagram.Pages[0].Nodes.OrderBy(n => n.Id)
            .Select(n => (n.Id, n.Bounds.X, n.Bounds.Y)).ToList();

        Assert.Equal(nodesA, nodesB);
    }

    [Fact]
    public void EditReplacesRatherThanDuplicates()
    {
        var dir = TestData.NewTempDir("tb-dup");
        var source = Path.Combine(dir, "a.vsdx");

        var engine = new VsdxEngine();
        engine.Create(WithText(), source);

        var model = engine.Read(source).Diagram;
        model.Pages[0].TextBlocks.Single(t => t.Id == "title").Text = "新标题";

        var output = Path.Combine(dir, "b.vsdx");
        engine.Update(source, model, output);

        var page = engine.Read(output).Diagram.Pages[0];
        Assert.Equal(4, page.TextBlocks.Count);
        Assert.Equal("新标题", page.TextBlocks.Single(t => t.Id == "title").Text);
    }

    [Fact]
    public void TextBlocksRendersWithoutError()
    {
        var dir = TestData.NewTempDir("tb-render");
        var source = Path.Combine(dir, "t.vsdx");
        var png = Path.Combine(dir, "t.png");

        var engine = new VsdxEngine();
        engine.Create(WithText(), source);
        var render = engine.Render(new RenderRequest
        {
            InputPath = source, OutputPath = png, Format = "png", Dpi = 96
        });

        Assert.Single(render.Pages);
        Assert.True(new FileInfo(png).Length > 1000);
    }

    [Fact]
    public void EmptyTextIsSkipped()
    {
        var dir = TestData.NewTempDir("tb-empty");
        var diagram = WithText();
        diagram.Pages[0].TextBlocks.Add(new TextBlock
        {
            Id = "blank", Text = "", Bounds = new Bounds(5, 5, 2, 0.3)
        });

        var path = Path.Combine(dir, "t.vsdx");
        new VsdxEngine().Create(diagram, path);

        var doc = FidelityTests.ReadPage(path);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
        Assert.Equal(4, doc.Descendants(ns + "Shape")
            .Count(s => (string?)s.Attribute("NameU") == "TextBlock"));
    }

    [Fact]
    public void TextAndPathsCoexistWithNodes()
    {
        var dir = TestData.NewTempDir("tb-coexist");
        var diagram = WithText();
        diagram.Pages[0].Paths.Add(new FreePath
        {
            Id = "rule",
            Bounds = new Bounds(5.5, 6.3, 6, 0.2),
            Geometry =
            {
                new PathGeometry
                {
                    Rows =
                    {
                        new PathRow { Type = "MoveTo", Cells = new() { ["X"] = "0", ["Y"] = "0" } },
                        new PathRow { Type = "LineTo", Cells = new() { ["X"] = "6", ["Y"] = "0" } }
                    }
                }
            }
        });

        var path = Path.Combine(dir, "t.vsdx");
        var engine = new VsdxEngine();
        engine.Create(diagram, path);
        var page = engine.Read(path).Diagram.Pages[0];

        Assert.Equal(2, page.Nodes.Count);
        Assert.Equal(4, page.TextBlocks.Count);
        Assert.Single(page.Paths);
    }
}
