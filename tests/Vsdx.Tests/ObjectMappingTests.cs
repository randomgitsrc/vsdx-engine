using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Object mapping is the bridge between protocol ids and the shapes in the package.
///
/// It covered nodes and containers only. Edges and text blocks were absent even on a single page:
/// a diagram with two nodes, one container, one edge and one text block reported three entries.
/// Anything driving incremental edits from this map could address nodes but not the connectors or
/// labels attached to them, so "rename this edge" or "move that title" had no route from IR id to
/// shape.
///
/// Multi-page was worse: the map was built from Pages.FirstOrDefault(), so a paged IR — whose
/// shorthand node lists are empty — produced an empty map despite the diagram being written.
/// </summary>
public sealed class ObjectMappingTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string SinglePageIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "containers":[{"id":"g1","label":"Layer"}],
     "nodes":[{"id":"a","label":"A","containerId":"g1"},{"id":"b","label":"B","containerId":"g1"}],
     "edges":[{"id":"e1","from":"a","to":"b","label":"calls"}],
     "textBlocks":[{"id":"t1","text":"Title","role":"title",
                    "box":{"x":3,"y":5,"width":3,"height":0.4}}]}
    """;

    private static ExecutionResult Run(string irJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-map-{Guid.NewGuid():N}.vsdx");
        try
        {
            return new ProtocolRunner().Execute(ir, null, path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MappingCoversNodesAndContainers()
    {
        var result = Run(SinglePageIr);

        Assert.Contains("a", result.ObjectMapping.Keys);
        Assert.Contains("b", result.ObjectMapping.Keys);
        Assert.Contains("g1", result.ObjectMapping.Keys);
    }

    [Fact]
    public void MappingCoversEdges()
    {
        var result = Run(SinglePageIr);

        Assert.Contains("e1", result.ObjectMapping.Keys);
    }

    [Fact]
    public void MappingCoversTextBlocks()
    {
        var result = Run(SinglePageIr);

        Assert.Contains("t1", result.ObjectMapping.Keys);
    }

    [Fact]
    public void MappingCoversEveryDeclaredObject()
    {
        var result = Run(SinglePageIr);

        // Exactly the set the IR declared — no gaps, and no invented entries either.
        var expected = new[] { "a", "b", "g1", "e1", "t1" }.OrderBy(x => x, StringComparer.Ordinal);
        var actual = result.ObjectMapping.Keys.OrderBy(x => x, StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PagedIrIsMappedToo()
    {
        // The shorthand node lists are empty for paged input, so a first-page-only implementation
        // returned nothing at all here.
        const string pagedIr = """
        {"irVersion":"1.0","id":"p","kind":"architecture",
         "pages":[
          {"id":"pg1",
           "nodes":[{"id":"a1","label":"A1"},{"id":"b1","label":"B1"}],
           "edges":[{"id":"e1","from":"a1","to":"b1"}]},
          {"id":"pg2",
           "nodes":[{"id":"a2","label":"A2"},{"id":"b2","label":"B2"}],
           "edges":[{"id":"e2","from":"a2","to":"b2"}]}]}
        """;

        var result = Run(pagedIr);

        Assert.Contains("a1", result.ObjectMapping.Keys);
        Assert.Contains("b1", result.ObjectMapping.Keys);
        Assert.Contains("a2", result.ObjectMapping.Keys);
        Assert.Contains("b2", result.ObjectMapping.Keys);
        Assert.Contains("e1", result.ObjectMapping.Keys);
        Assert.Contains("e2", result.ObjectMapping.Keys);
    }

    [Fact]
    public void PagesOnTheSecondSheetAreNotSilentlyDropped()
    {
        var result = Run("""
        {"irVersion":"1.0","id":"p","kind":"architecture",
         "pages":[
          {"id":"pg1","nodes":[{"id":"only1","label":"One"}],"edges":[]},
          {"id":"pg2","nodes":[{"id":"only2","label":"Two"}],"edges":[]}]}
        """);

        // A missing entry here would mean the caller cannot address half the document.
        Assert.Contains("only1", result.ObjectMapping.Keys);
        Assert.Contains("only2", result.ObjectMapping.Keys);
    }
}
