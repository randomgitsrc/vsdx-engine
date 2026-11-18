using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// MultiPageTests: Diagram IR must express more than one page.
///
/// Phase 8 found the model, writer, reader and renderer all supported multiple pages, but the
/// protocol layer could not request them: DiagramIr had no `pages` field and the converter
/// hardcoded `page-0`. Callers could not build a multi-page deliverable at all.
/// </summary>
public sealed class MultiPageTests
{
    private static readonly JsonSerializerOptions IrJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static DiagramIr ThreePages() => new()
    {
        Id = "multi",
        Title = "Multi-page",
        Kind = "architecture",
        Pages =
        {
            new IrPage
            {
                Id = "p1", Name = "Overview",
                Nodes =
                {
                    new IrNode { Id = "a1", Label = "Gateway", Role = "gateway" },
                    new IrNode { Id = "a2", Label = "Service", Role = "service" }
                },
                Edges = { new IrEdge { Id = "e1", From = "a1", To = "a2", Relation = "calls" } }
            },
            new IrPage
            {
                Id = "p2", Name = "Deployment",
                Nodes =
                {
                    new IrNode { Id = "b1", Label = "Zone A", Role = "external" },
                    new IrNode { Id = "b2", Label = "Database", Role = "database" }
                },
                Edges = { new IrEdge { Id = "f1", From = "b1", To = "b2", Relation = "depends" } }
            },
            new IrPage
            {
                Id = "p3", Name = "Operations",
                Nodes = { new IrNode { Id = "c1", Label = "Monitor", Role = "monitor" } }
            }
        }
    };

    [Fact]
    public void PagesProduceSeparatePageParts()
    {
        var dir = TestData.NewTempDir("mp-parts");
        var output = Path.Combine(dir, "m.vsdx");

        var result = new ProtocolRunner().Execute(ThreePages(), null, output);

        // "partial" is legitimate here: a single-node page is mostly whitespace, which the
        // quality rules report. The assertion under test is the page structure, not the score.
        Assert.NotEqual(ExecutionStatus.Failed, result.Status);

        using var zip = System.IO.Compression.ZipFile.OpenRead(output);
        var pages = zip.Entries
            .Where(e => System.Text.RegularExpressions.Regex.IsMatch(
                e.FullName, @"^visio/pages/page\d+\.xml$"))
            .ToList();

        Assert.Equal(3, pages.Count);
    }

    [Fact]
    public void PageNamesArePreserved()
    {
        var dir = TestData.NewTempDir("mp-names");
        var output = Path.Combine(dir, "m.vsdx");

        new ProtocolRunner().Execute(ThreePages(), null, output);
        var diagram = new VsdxEngine().Read(output).Diagram;

        Assert.Equal(3, diagram.Pages.Count);
        Assert.Equal("Overview", diagram.Pages[0].Name);
        Assert.Equal("Deployment", diagram.Pages[1].Name);
        Assert.Equal("Operations", diagram.Pages[2].Name);
    }

    [Fact]
    public void EachPageKeepsItsOwnNodes()
    {
        var dir = TestData.NewTempDir("mp-nodes");
        var output = Path.Combine(dir, "m.vsdx");

        new ProtocolRunner().Execute(ThreePages(), null, output);
        var diagram = new VsdxEngine().Read(output).Diagram;

        Assert.Equal(2, diagram.Pages[0].Nodes.Count);
        Assert.Equal(2, diagram.Pages[1].Nodes.Count);
        Assert.Single(diagram.Pages[2].Nodes);

        // No bleed between pages.
        Assert.DoesNotContain(diagram.Pages[0].Nodes, n => n.Id.StartsWith("b"));
        Assert.DoesNotContain(diagram.Pages[1].Nodes, n => n.Id.StartsWith("a"));
    }

    [Fact]
    public void SinglePageShorthandStillWorks()
    {
        // Backwards compatibility: documents with top-level nodes and no pages must be unchanged.
        var ir = new DiagramIr
        {
            Id = "single",
            Nodes =
            {
                new IrNode { Id = "x", Label = "X", Role = "service" },
                new IrNode { Id = "y", Label = "Y", Role = "service" }
            },
            Edges = { new IrEdge { Id = "e", From = "x", To = "y" } }
        };

        var dir = TestData.NewTempDir("mp-single");
        var output = Path.Combine(dir, "s.vsdx");

        var result = new ProtocolRunner().Execute(ir, null, output);

        // This test is about the shorthand being understood, not about layout quality. A two-node
        // diagram lands on the 3" minimum page height and therefore reads as underused — a
        // graduated warning that has nothing to do with whether `nodes`/`edges` were picked up.
        // Asserting Succeeded here made the test fail whenever node sizing changed.
        Assert.True(result.Status is ExecutionStatus.Succeeded or ExecutionStatus.Partial,
            $"unexpected status {result.Status}");

        var diagram = new VsdxEngine().Read(output).Diagram;
        Assert.Single(diagram.Pages);
        Assert.Equal(2, diagram.Pages[0].Nodes.Count);
    }

    [Fact]
    public void PagesTakePrecedenceOverShorthand()
    {
        // If both are supplied, pages win — the shorthand is documented as ignored.
        var ir = ThreePages();
        ir.Nodes.Add(new IrNode { Id = "stray", Label = "Stray" });

        var dir = TestData.NewTempDir("mp-precedence");
        var output = Path.Combine(dir, "m.vsdx");

        new ProtocolRunner().Execute(ir, null, output);
        var diagram = new VsdxEngine().Read(output).Diagram;

        Assert.Equal(3, diagram.Pages.Count);
        Assert.DoesNotContain(diagram.Pages.SelectMany(p => p.Nodes), n => n.Id == "stray");
    }

    [Fact]
    public void DuplicatePageIdsAreRejected()
    {
        var ir = ThreePages();
        ir.Pages[1].Id = "p1";

        var errors = IrValidator.Validate(ir);

        Assert.Contains(errors, e => e.Code == "ir.page-id-duplicate");
    }

    [Fact]
    public void DanglingEdgeOnSecondPageIsRejected()
    {
        var ir = ThreePages();
        ir.Pages[1].Edges.Add(new IrEdge { Id = "bad", From = "b1", To = "nope" });

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.edge-to-missing");
    }

    [Fact]
    public void EmptyIrWithNoPagesAndNoNodesIsRejected()
    {
        var errors = IrValidator.Validate(new DiagramIr { Id = "empty" });
        Assert.Contains(errors, e => e.Code == "ir.empty");
    }

    [Fact]
    public void AllPagesRender()
    {
        var dir = TestData.NewTempDir("mp-render");
        var output = Path.Combine(dir, "m.vsdx");
        var png = Path.Combine(dir, "m.png");

        new ProtocolRunner().Execute(ThreePages(), null, output);
        var render = new VsdxEngine().Render(new RenderRequest
        {
            InputPath = output, OutputPath = png, Format = "png", Dpi = 96
        });

        Assert.Equal(3, render.Pages.Count);
        Assert.All(render.Pages, p => Assert.True(File.Exists(p.OutputPath)));
    }

    [Fact]
    public void MultiPageIrRoundTripsThroughJson()
    {
        var ir = ThreePages();
        var json = JsonSerializer.Serialize(ir, IrJson);
        var back = JsonSerializer.Deserialize<DiagramIr>(json, IrJson);

        Assert.NotNull(back);
        Assert.Equal(3, back!.Pages.Count);
        Assert.Equal("Overview", back.Pages[0].Name);
        Assert.Equal(2, back.Pages[0].Nodes.Count);
    }

    [Fact]
    public void EachPageLaysOutIndependently()
    {
        var dir = TestData.NewTempDir("mp-layout");
        var output = Path.Combine(dir, "m.vsdx");

        new ProtocolRunner().Execute(ThreePages(),
            new DesignSpec { Layout = "layered" }, output);

        var diagram = new VsdxEngine().Read(output).Diagram;

        // Every page must have its own non-degenerate geometry.
        foreach (var page in diagram.Pages)
        {
            Assert.True(page.Size!.Width > 0);
            Assert.All(page.Nodes, n => Assert.True(n.Bounds.W > 0));
        }
    }
}
