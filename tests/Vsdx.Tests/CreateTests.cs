using System.IO.Compression;
using System.Xml.Linq;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Validator;
using Xunit;

namespace Vsdx.Tests;

/// <summary>CreateTests: the engine produces a real, valid VSDX package.</summary>
public sealed class CreateTests
{
    [Fact]
    public void Create_ProducesZipPackage()
    {
        var dir = TestData.NewTempDir("create");
        var path = Path.Combine(dir, "out.vsdx");

        var engine = new VsdxEngine();
        var result = engine.Create(TestData.SmallModel(), path);

        Assert.True(File.Exists(path));
        Assert.True(result.FileSize > 0);
        Assert.Empty(result.Compatibility.Errors);

        using var zip = ZipFile.OpenRead(path);
        Assert.Contains(zip.Entries, e => e.FullName == "[Content_Types].xml");
        Assert.Contains(zip.Entries, e => e.FullName == "_rels/.rels");
        Assert.Contains(zip.Entries, e => e.FullName == "visio/document.xml");
        Assert.Contains(zip.Entries, e => e.FullName == "visio/pages/pages.xml");
    }

    [Fact]
    public void Create_IsNotRenamedSvgOrPng()
    {
        var dir = TestData.NewTempDir("create-notfake");
        var path = Path.Combine(dir, "out.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);

        using var stream = File.OpenRead(path);
        Span<byte> magic = stackalloc byte[4];
        stream.Read(magic);
        // ZIP local file header
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, magic.ToArray());
    }

    [Fact]
    public void Create_WritesAllNodesAndEdges()
    {
        var dir = TestData.NewTempDir("create-counts");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();

        var result = engine.Create(TestData.SmallModel(), path);
        Assert.Equal(3, result.ShapesWritten);
        Assert.Equal(2, result.ConnectorsWritten);
        Assert.Equal(1, result.PagesWritten);

        var read = engine.Read(path);
        Assert.Equal(3, read.Diagram.Pages[0].Nodes.Count);
        Assert.Equal(2, read.Diagram.Pages[0].Edges.Count);
    }

    [Fact]
    public void Create_WritesShapeData()
    {
        var dir = TestData.NewTempDir("create-data");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path);

        var node = engine.Read(path).Diagram.Pages[0].Nodes.First(n => n.Id == "n_a");
        Assert.NotNull(node.ShapeData);
        Assert.Equal("team-a", node.ShapeData!["owner"]);
        Assert.Equal("1", node.ShapeData["tier"]);
    }

    [Fact]
    public void Create_WritesRealContainerGroup()
    {
        var dir = TestData.NewTempDir("create-group");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path);

        var page = engine.Read(path).Diagram.Pages[0];
        Assert.Single(page.Groups);
        Assert.Equal(2, page.Groups[0].Members.Count);

        // The container must be a real Visio container shape, not a plain rectangle.
        using var zip = ZipFile.OpenRead(path);
        var pageEntry = zip.Entries.First(e => e.FullName == "visio/pages/page1.xml");
        using var reader = new StreamReader(pageEntry.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("msvStructureType", xml);
        Assert.Contains("Container", xml);
    }

    [Fact]
    public void Create_SupportsMultiplePagePrimitives()
    {
        var dir = TestData.NewTempDir("create-shapes");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();

        var model = new Diagram
        {
            PageSize = new PageSize { Width = 14, Height = 9, Unit = "inches" },
            Pages =
            {
                new Page
                {
                    Id = "p", Name = "Shapes",
                    Size = new PageSize { Width = 14, Height = 9, Unit = "inches" },
                    Nodes =
                    {
                        new Node { Id = "r", Text = "Rectangle", Shape = "Rectangle", Bounds = new Bounds(1.5, 7, 1.5, 0.8) },
                        new Node { Id = "c", Text = "Circle", Shape = "Circle", Bounds = new Bounds(3.5, 7, 0.9, 0.9) },
                        new Node { Id = "d", Text = "Diamond", Shape = "Diamond", Bounds = new Bounds(5.5, 7, 1.5, 1.0) },
                        new Node { Id = "rd", Text = "Rounded", Shape = "rounded", Bounds = new Bounds(7.5, 7, 1.5, 0.8) },
                        new Node { Id = "t", Text = "TextBox", Shape = "Text", Bounds = new Bounds(9.5, 7, 1.5, 0.6) },
                        new Node { Id = "p", Text = "Process", Shape = "Process", Bounds = new Bounds(11.5, 7, 1.5, 0.8) },
                        new Node { Id = "h", Text = "Hexagon", Shape = "Hexagon", Bounds = new Bounds(1.5, 5, 1.5, 0.8) }
                    }
                }
            }
        };

        var result = engine.Create(model, path);
        Assert.Empty(result.Compatibility.Errors);
        Assert.Equal(7, engine.Read(path).Diagram.Pages[0].Nodes.Count);
    }

    [Fact]
    public void Create_RejectsUnknownOutputDirectoryGracefully()
    {
        var dir = TestData.NewTempDir("create-nested");
        var path = Path.Combine(dir, "a", "b", "c", "out.vsdx");
        var engine = new VsdxEngine();
        var result = engine.Create(TestData.SmallModel(), path);
        Assert.True(File.Exists(path), "nested output directories should be created");
        Assert.Empty(result.Compatibility.Errors);
    }
}
