using Vsdx.Engine;
using System.Text.Json;
using Vsdx.Protocol;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// An edge can only carry an arrowhead at its target, so a genuinely bidirectional relationship is drawn pointing
/// one way while its label claims both.
///
/// Measured on the `netflix-active-active` case: the edge labelled "双向复制" has `EndArrow=13` and **no**
/// `BeginArrow` at all. `Edge.BeginArrow` exists in the model and `OfficeImoDocumentWriter` writes it, but `IrEdge`
/// has no field for it — so the `execute` path cannot express a two-headed connector even though everything
/// downstream can render one.
///
/// That is the same shape of gap as `LinePattern` and `Present` before it: the capability is there, the protocol
/// does not expose it, and the diagram quietly says something it does not mean.
/// </summary>
public sealed class BidirectionalArrowTests
{
    private static Edge Convert(string extra)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(
            $$"""
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
             "edges":[{"id":"e","from":"a","to":"b"{{extra}}}]}
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return DiagramIrConverter.ToDiagram(ir).Pages[0].Edges[0];
    }

    [Fact]
    public void ABidirectionalEdgeCarriesArrowsAtBothEnds()
    {
        var edge = Convert(""","bidirectional":true""");

        Assert.Equal("triangle", edge.EndArrow);
        Assert.False(string.IsNullOrEmpty(edge.BeginArrow), "no arrowhead at the start end");
        Assert.NotEqual("none", edge.BeginArrow);
    }

    [Fact]
    public void AnOrdinaryEdgeKeepsOneArrowhead()
    {
        var edge = Convert("");

        Assert.Equal("triangle", edge.EndArrow);
        Assert.True(edge.BeginArrow is null or "none");
    }

    [Fact]
    public void AContainmentEdgeIsStillArrowlessBothWays()
    {
        var edge = Convert(""","relation":"contains","bidirectional":true""");

        Assert.Equal("none", edge.EndArrow);
        Assert.True(edge.BeginArrow is null or "none");
    }

    [Fact]
    public void TheStartArrowheadReachesThePackage()
    {
        // `BeginArrow` is written by the writer already; this proves the IR path now reaches it.
        var ir = JsonSerializer.Deserialize<DiagramIr>(
            """
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
             "edges":[{"id":"e","from":"a","to":"b","bidirectional":true}]}
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-bi-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(DiagramIrConverter.ToDiagram(ir), output, new WriteOptions { AutoLayout = true });

            using var zip = System.IO.Compression.ZipFile.OpenRead(output);
            using var reader = new StreamReader(zip.Entries
                .First(e => e.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)
                         && e.FullName != "visio/pages/pages.xml").Open());
            var xml = reader.ReadToEnd();

            Assert.Contains("BeginArrow", xml);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }
}
