using Vsdx.Adapters;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// FreePathTests: custom geometry must survive create, read, edit, save and re-read.
///
/// Phase 8 found these shapes were read back as `Node { Shape = "Rectangle" }` — the reader had
/// nowhere to put the geometry, so a custom outline silently became a rectangle. The reviewer
/// required that unsupported geometry instructions be preserved verbatim rather than converted.
/// </summary>
public sealed class FreePathTests
{
    private static Diagram Zigzag() => new()
    {
        Id = "fp", Name = "FreePath", Type = "architecture",
        PageSize = new PageSize { Width = 10, Height = 8 },
        Pages =
        {
            new Page
            {
                Name = "P", Size = new PageSize { Width = 10, Height = 8 },
                Nodes = { new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 6, 2, 0.9) } },
                Paths =
                {
                    new FreePath
                    {
                        Id = "zig",
                        Name = "Zigzag",
                        Bounds = new Bounds(5, 3, 4, 1.5),
                        Style = new ShapeStyle { Stroke = "#C00000", StrokeWidth = 0.02 },
                        Geometry =
                        {
                            new PathGeometry
                            {
                                Index = 0,
                                SectionCells = new Dictionary<string, string>
                                {
                                    ["NoFill"] = "1", ["NoLine"] = "0"
                                },
                                Rows =
                                {
                                    new PathRow { Type = "MoveTo", Cells = new() { ["X"] = "0", ["Y"] = "0" } },
                                    new PathRow { Type = "LineTo", Cells = new() { ["X"] = "1.33", ["Y"] = "1.5" } },
                                    new PathRow { Type = "LineTo", Cells = new() { ["X"] = "2.66", ["Y"] = "0" } },
                                    new PathRow { Type = "LineTo", Cells = new() { ["X"] = "4", ["Y"] = "1.5" } }
                                }
                            }
                        }
                    }
                }
            }
        }
    };

    [Fact]
    public void PathIsWrittenWithGeometry()
    {
        var dir = TestData.NewTempDir("fp-write");
        var path = Path.Combine(dir, "fp.vsdx");

        new VsdxEngine().Create(Zigzag(), path);

        Assert.True(FidelityTests.GeometryRows(path) >= 4);
    }

    [Fact]
    public void PathReadsBackAsPathNotRectangle()
    {
        var dir = TestData.NewTempDir("fp-read");
        var path = Path.Combine(dir, "fp.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Zigzag(), path);
        var page = engine.Read(path).Diagram.Pages[0];

        // The decisive assertion: this must not become a Node.
        Assert.Single(page.Paths);
        Assert.Single(page.Nodes);          // only "a"
        Assert.Equal("a", page.Nodes[0].Id);
        Assert.Equal("zig", page.Paths[0].Id);
    }

    [Fact]
    public void GeometryRowsSurviveRoundTrip()
    {
        var dir = TestData.NewTempDir("fp-rt");
        var path = Path.Combine(dir, "fp.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Zigzag(), path);
        var page = engine.Read(path).Diagram.Pages[0];

        var rows = page.Paths[0].Geometry[0].Rows;
        Assert.Equal(4, rows.Count);
        Assert.Equal("MoveTo", rows[0].Type);
        Assert.Equal("1.33", rows[1].Cells["X"]);
        Assert.Equal("1.5", rows[1].Cells["Y"]);
    }

    [Fact]
    public void EditedGeometryIsPersisted()
    {
        var dir = TestData.NewTempDir("fp-edit");
        var source = Path.Combine(dir, "a.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Zigzag(), source);

        var model = engine.Read(source).Diagram;
        model.Pages[0].Paths[0].Geometry[0].Rows[3].Cells["Y"] = "2.0";

        var output = Path.Combine(dir, "b.vsdx");
        engine.Update(source, model, output);

        var reread = engine.Read(output).Diagram.Pages[0];
        Assert.Equal("2.0", reread.Paths[0].Geometry[0].Rows[3].Cells["Y"]);
    }

    [Fact]
    public void EditDoesNotDuplicateThePath()
    {
        // The editor cannot see geometry shapes as model objects, so a naive write would emit the
        // edited path alongside its old version.
        var dir = TestData.NewTempDir("fp-dup");
        var source = Path.Combine(dir, "a.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Zigzag(), source);

        var model = engine.Read(source).Diagram;
        model.Pages[0].Paths[0].Geometry[0].Rows[3].Cells["Y"] = "2.0";

        var output = Path.Combine(dir, "b.vsdx");
        engine.Update(source, model, output);

        Assert.Single(engine.Read(output).Diagram.Pages[0].Paths);
    }

    [Fact]
    public void UnsupportedInstructionsArePreservedVerbatim()
    {
        // ArcTo is not interpreted by the engine, but dropping it would turn a curve into a
        // straight line — a silent geometry change.
        var dir = TestData.NewTempDir("fp-unsupported");
        var source = Path.Combine(dir, "a.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Zigzag(), source);

        var model = engine.Read(source).Diagram;
        model.Pages[0].Paths[0].Geometry[0].Rows.Add(new PathRow
        {
            Type = "ArcTo",
            Cells = new Dictionary<string, string>
            {
                ["X"] = "5", ["Y"] = "0", ["A"] = "1", ["B"] = "0.5"
            }
        });

        var output = Path.Combine(dir, "b.vsdx");
        engine.Update(source, model, output);

        var reread = engine.Read(output).Diagram.Pages[0];
        var rows = reread.Paths[0].Geometry[0].Rows;

        var arc = rows.SingleOrDefault(r => r.Type == "ArcTo");
        Assert.NotNull(arc);
        Assert.Equal("5", arc!.Cells["X"]);
        Assert.Equal("0.5", arc.Cells["B"]);
    }

    [Fact]
    public void PathKeepsItsIdentityAcrossRoundTrips()
    {
        var dir = TestData.NewTempDir("fp-id");
        var source = Path.Combine(dir, "a.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Zigzag(), source);
        var originalId = engine.Read(source).Diagram.Pages[0].Paths[0].VisioShapeId;

        var output = Path.Combine(dir, "b.vsdx");
        engine.Update(source, engine.Read(source).Diagram, output);

        Assert.Equal(originalId, engine.Read(output).Diagram.Pages[0].Paths[0].VisioShapeId);
    }

    [Fact]
    public void PathRendersWithoutError()
    {
        var dir = TestData.NewTempDir("fp-render");
        var source = Path.Combine(dir, "a.vsdx");
        var png = Path.Combine(dir, "a.png");

        var engine = new VsdxEngine();
        engine.Create(Zigzag(), source);
        var render = engine.Render(new RenderRequest
        {
            InputPath = source, OutputPath = png, Format = "png", Dpi = 96
        });

        Assert.Single(render.Pages);
        Assert.True(new FileInfo(png).Length > 1000);
    }

    [Fact]
    public void PathsAndNodesCoexist()
    {
        var dir = TestData.NewTempDir("fp-coexist");
        var path = Path.Combine(dir, "fp.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Zigzag(), path);
        var page = engine.Read(path).Diagram.Pages[0];

        Assert.Single(page.Nodes);
        Assert.Single(page.Paths);
        // Both participate in the z-order list.
        Assert.Equal(2, page.ShapeOrder!.Count);
    }

    [Fact]
    public void EmptyDiagramHasNoPaths()
    {
        var dir = TestData.NewTempDir("fp-none");
        var path = Path.Combine(dir, "n.vsdx");

        var engine = new VsdxEngine();
        engine.Create(new Diagram
        {
            Id = "n", Name = "N",
            PageSize = new PageSize { Width = 8, Height = 6 },
            Pages =
            {
                new Page
                {
                    Name = "P", Size = new PageSize { Width = 8, Height = 6 },
                    Nodes = { new Node { Id = "x", Text = "X", Bounds = new Bounds(4, 3, 2, 1) } }
                }
            }
        }, path);

        Assert.Empty(engine.Read(path).Diagram.Pages[0].Paths);
    }

    [Fact]
    public void ThreeGenerationsKeepGeometry()
    {
        var dir = TestData.NewTempDir("fp-gen");
        var current = Path.Combine(dir, "g0.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Zigzag(), current);

        for (int i = 1; i <= 3; i++)
        {
            var next = Path.Combine(dir, $"g{i}.vsdx");
            engine.Update(current, engine.Read(current).Diagram, next);
            current = next;
        }

        var rows = engine.Read(current).Diagram.Pages[0].Paths[0].Geometry[0].Rows;
        Assert.Equal(4, rows.Count);
        Assert.All(rows.Take(4), r => Assert.Contains(r.Type, new[] { "MoveTo", "LineTo" }));
    }
}
