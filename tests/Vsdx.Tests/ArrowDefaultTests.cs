using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// An edge that names no relation gets no arrowhead, so every diagram in this project was drawn with directionless
/// lines.
///
/// `RelationUsesArrow` is `relation is not ("contains" or null)`. `null` is in the exclusion list, so an edge that
/// only carries a `label` — which is every edge in all six reference scenarios — produces `EndArrow = "none"`.
///
/// Verified in the package: `grep -c EndArrow` on `page1.xml` returns **0** for `cbo.vsdx`. An independent visual
/// reviewer reached the same conclusion from the rendered images before seeing any of this, and rated both
/// diagrams unqualified on that basis alone: a layered diagram whose connector direction is unknowable is not a
/// professional architecture diagram, whatever its layout score says.
///
/// The default should be an arrow. `contains` is the one relation that genuinely has no direction.
/// </summary>
public sealed class ArrowDefaultTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("calls", true)]
    [InlineData("reads", true)]
    [InlineData("flows", true)]
    [InlineData("depends", true)]
    [InlineData("contains", false)]
    public void OnlyContainsHasNoArrow(string? relation, bool expected)
    {
        Assert.Equal(expected, DiagramIrConverter.RelationUsesArrow(relation));
    }

    [Fact]
    public void AnEdgeWithOnlyALabelProducesAnArrowhead()
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            """
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
             "edges":[{"id":"e","from":"a","to":"b","label":"HTTPS"}]}
            """,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var diagram = DiagramIrConverter.ToDiagram(ir);

        Assert.Equal("triangle", diagram.Pages[0].Edges[0].EndArrow);
    }

    [Fact]
    public void AContainmentEdgeStillHasNoArrow()
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            """
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
             "edges":[{"id":"e","from":"a","to":"b","relation":"contains"}]}
            """,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var diagram = DiagramIrConverter.ToDiagram(ir);

        Assert.Equal("none", diagram.Pages[0].Edges[0].EndArrow);
    }

    [Fact]
    public void TheWrittenPackageContainsArrowCells()
    {
        // The end-to-end proof: the cell has to reach the XML, not just the in-memory model.
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            """
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
             "edges":[{"id":"e","from":"a","to":"b","label":"HTTPS"}]}
            """,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-arrow-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(DiagramIrConverter.ToDiagram(ir), output, new WriteOptions { AutoLayout = true });

            using var zip = System.IO.Compression.ZipFile.OpenRead(output);
            using var reader = new StreamReader(zip.Entries
                .First(e => e.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)
                         && e.FullName != "visio/pages/pages.xml").Open());
            var xml = reader.ReadToEnd();

            Assert.Contains("EndArrow", xml);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }
}
