using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// ProtocolObjectsTests: the two object types added in 8.1 must be reachable through the protocol.
///
/// They were modelled and round-tripped, but DiagramIr had no field for them, so an agent using
/// the protocol could not create a title or a custom path at all — the JSON was accepted and the
/// objects were silently dropped. That is the exact failure mode the project's own contract
/// forbids: a declared input is either honoured or reported.
/// </summary>
public sealed class ProtocolObjectsTests
{
    private static readonly JsonSerializerOptions IrJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static DiagramIr WithTextAndPath() => new()
    {
        Id = "pobj",
        Title = "Protocol objects",
        Kind = "architecture",
        Nodes =
        {
            new IrNode { Id = "a", Label = "A", Role = "service" },
            new IrNode { Id = "b", Label = "B", Role = "database" }
        },
        Edges = { new IrEdge { Id = "e", From = "a", To = "b" } },
        TextBlocks =
        {
            new IrTextBlock
            {
                Id = "title", Text = "My Diagram", Role = "title",
                Box = new IrBox { X = 5.5, Y = 8.5, Width = 6, Height = 0.6 },
                Align = "center", Bold = true
            },
            new IrTextBlock
            {
                Id = "legend", Text = "blue = service", Role = "legend",
                Box = new IrBox { X = 5.5, Y = 1.2, Width = 6, Height = 0.35 },
                Align = "center"
            }
        },
        Paths =
        {
            new IrPath
            {
                Id = "rule", Name = "Divider",
                Box = new IrBox { X = 5.5, Y = 7.6, Width = 8, Height = 0.05 },
                Stroke = "#999999", StrokeWidth = 0.012,
                Rows =
                {
                    new IrPathRow { Type = "MoveTo", Cells = new() { ["X"] = "0", ["Y"] = "0" } },
                    new IrPathRow { Type = "LineTo", Cells = new() { ["X"] = "8", ["Y"] = "0" } }
                }
            }
        }
    };

    [Fact]
    public void TextBlocksReachTheVsdx()
    {
        var dir = TestData.NewTempDir("po-text");
        var output = Path.Combine(dir, "o.vsdx");

        var result = new ProtocolRunner().Execute(WithTextAndPath(), null, output);
        Assert.Equal(ExecutionStatus.Succeeded, result.Status);

        using var zip = System.IO.Compression.ZipFile.OpenRead(output);
        using var reader = new StreamReader(zip.GetEntry("visio/pages/page1.xml")!.Open());
        var xml = reader.ReadToEnd();

        Assert.Contains("NameU=\"TextBlock\"", xml);
    }

    [Fact]
    public void PathsReachTheVsdx()
    {
        var dir = TestData.NewTempDir("po-path");
        var output = Path.Combine(dir, "o.vsdx");

        new ProtocolRunner().Execute(WithTextAndPath(), null, output);

        using var zip = System.IO.Compression.ZipFile.OpenRead(output);
        using var reader = new StreamReader(zip.GetEntry("visio/pages/page1.xml")!.Open());
        var xml = reader.ReadToEnd();

        Assert.Contains("NameU=\"FreePath\"", xml);
    }

    [Fact]
    public void BothReadBackOntoTheirOwnCollections()
    {
        var dir = TestData.NewTempDir("po-read");
        var output = Path.Combine(dir, "o.vsdx");

        new ProtocolRunner().Execute(WithTextAndPath(), null, output);
        var page = new VsdxEngine().Read(output).Diagram.Pages[0];

        Assert.Equal(2, page.TextBlocks.Count);
        Assert.Single(page.Paths);
        Assert.Equal(2, page.Nodes.Count);      // the title must not become a node
        Assert.Contains(page.TextBlocks, t => t.Id == "title" && t.Text == "My Diagram");
    }

    [Fact]
    public void UnsplashInputFieldsAreNotSilentlyDropped()
    {
        // The regression this guards: unknown JSON fields used to vanish without a word.
        var json = JsonSerializer.Serialize(WithTextAndPath(), IrJson);
        var back = JsonSerializer.Deserialize<DiagramIr>(json, IrJson);

        Assert.NotNull(back);
        Assert.Equal(2, back!.TextBlocks.Count);
        Assert.Single(back.Paths);
    }

    [Fact]
    public void TextBlockWithoutBoxIsRejected()
    {
        // Text is not auto-laid-out, so a position is required. Accepting a missing box would
        // place the title at an arbitrary spot.
        var ir = WithTextAndPath();
        ir.TextBlocks[0].Box = null;

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.text-box");
    }

    [Fact]
    public void PathWithoutRowsIsRejected()
    {
        var ir = WithTextAndPath();
        ir.Paths[0].Rows.Clear();

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.path-no-rows");
    }

    [Fact]
    public void PathMustStartWithMoveTo()
    {
        var ir = WithTextAndPath();
        ir.Paths[0].Rows[0] = new IrPathRow { Type = "LineTo", Cells = new() { ["X"] = "1", ["Y"] = "1" } };

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.path-start");
    }

    [Fact]
    public void IdCollisionAcrossCollectionsIsRejected()
    {
        // Z-order is a single list of ids, so a node and a text block sharing an id are ambiguous.
        var ir = WithTextAndPath();
        ir.TextBlocks[0].Id = "a";

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.id-collision");
    }

    [Fact]
    public void DuplicateTextIdsAreRejected()
    {
        var ir = WithTextAndPath();
        ir.TextBlocks[1].Id = ir.TextBlocks[0].Id;

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.text-id-duplicate");
    }

    [Fact]
    public void EmptyTextIsRejected()
    {
        var ir = WithTextAndPath();
        ir.TextBlocks[0].Text = "";

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.text-empty");
    }

    [Fact]
    public void MultiPageTextAndPathsWork()
    {
        // Pages take precedence over the shorthand, so the first page must be moved into Pages
        // explicitly — the shorthand is ignored once Pages is non-empty (documented behaviour).
        var shorthand = WithTextAndPath();
        var ir = new DiagramIr
        {
            Id = shorthand.Id,
            Title = shorthand.Title,
            Kind = shorthand.Kind,
            Pages =
            {
                new IrPage
                {
                    Id = "p1", Name = "First",
                    Nodes = shorthand.Nodes,
                    Edges = shorthand.Edges,
                    TextBlocks = shorthand.TextBlocks,
                    Paths = shorthand.Paths
                },
                new IrPage
                {
                    Id = "p2", Name = "Second",
                    Nodes = { new IrNode { Id = "c", Label = "C" } },
                    TextBlocks =
                    {
                        new IrTextBlock
                        {
                            Id = "t2", Text = "Second page", Role = "title",
                            Box = new IrBox { X = 4, Y = 7, Width = 5, Height = 0.5 }
                        }
                    }
                }
            }
        };

        var dir = TestData.NewTempDir("po-multi");
        var output = Path.Combine(dir, "o.vsdx");

        var result = new ProtocolRunner().Execute(ir, null, output);
        Assert.NotEqual(ExecutionStatus.Failed, result.Status);

        var diagram = new VsdxEngine().Read(output).Diagram;
        Assert.Equal(2, diagram.Pages.Count);
        Assert.Single(diagram.Pages[1].TextBlocks);
    }

    [Fact]
    public void DecorationsStayInsideThePage()
    {
        // A title placed beyond a shrinking page must be pulled back in, not left invisible.
        var dir = TestData.NewTempDir("po-clamp");
        var ir = WithTextAndPath();
        ir.TextBlocks[0].Box = new IrBox { X = 5, Y = 20, Width = 4, Height = 0.5 };

        var output = Path.Combine(dir, "o.vsdx");
        new ProtocolRunner().Execute(ir, new DesignSpec { Layout = "layered" }, output);

        var page = new VsdxEngine().Read(output).Diagram.Pages[0];
        var title = page.TextBlocks.Single(t => t.Id == "title");

        Assert.True(title.Bounds.Y + title.Bounds.H / 2 <= page.Size!.Height + 0.01,
            $"title at y={title.Bounds.Y:F2} is outside the {page.Size.Height:F2}\" page");
    }

    [Fact]
    public void FilledFalseProducesNoFill()
    {
        var ir = WithTextAndPath();
        ir.Paths[0].Filled = false;

        var diagram = DiagramIrConverter.ToDiagram(ir);
        var path = diagram.Pages[0].Paths.Single();

        Assert.Contains(path.Geometry[0].SectionCells!, kv => kv.Key == "NoFill" && kv.Value == "1");
    }
}
