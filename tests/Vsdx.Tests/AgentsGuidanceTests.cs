using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The gotchas in `AGENTS.md` must be true.
///
/// That file is the only thing a new session loads automatically, so a wrong gotcha there misleads
/// every future session in the same way. Each claim is reproduced here rather than trusted.
///
/// Two were wrong when first written and were corrected by measuring: `IrAnchor` does **not** need an
/// explicit `mode:"fixed"` (the property defaults to it), and the transpose only bites flat diagrams —
/// a contained page infers top-to-bottom regardless.
/// </summary>
public sealed class AgentsGuidanceTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static ExecutionResult Run(string irJson, string specJson, out Diagram diagram)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-agents-{Guid.NewGuid():N}.vsdx");
        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);
            diagram = new VsdxEngine().Read(path).Diagram;
            return result;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private const string Flat = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    [Fact]
    public void MetadataMustBeNestedNotTopLevel()
    {
        // A top-level `description` is dropped by JSON binding, which looks like the engine losing data.
        Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "metadata":{"description":"nested"},"nodes":[{"id":"a","label":"A"}]}
        """, """{"specVersion":"1.0"}""", out var nested);

        Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "description":"top","nodes":[{"id":"a","label":"A"}]}
        """, """{"specVersion":"1.0"}""", out var top);

        Assert.Equal("nested", nested.Metadata.Description);
        Assert.True(string.IsNullOrEmpty(top.Metadata.Description));
    }

    [Fact]
    public void AFlatDiagramDefaultsToHorizontal()
    {
        Run(Flat, """{"specVersion":"1.0","layout":"layered"}""", out var diagram);

        var ys = diagram.Pages[0].Nodes.Select(n => n.Bounds!.Y).Distinct().ToList();

        Assert.Single(ys);
    }

    [Fact]
    public void StatingTopToBottomMakesAFlatDiagramVertical()
    {
        Run(Flat, """{"specVersion":"1.0","layout":"layered","direction":"topToBottom"}""", out var diagram);

        var xs = diagram.Pages[0].Nodes.Select(n => n.Bounds!.X).Distinct().ToList();

        Assert.Single(xs);
    }

    [Fact]
    public void AContainedDiagramInfersVerticalEitherWay()
    {
        // The claim in AGENTS.md: with containers the direction is inferred, so stating it changes
        // nothing. Verified rather than asserted.
        const string contained = """
        {"irVersion":"1.0","id":"t","kind":"workflow",
         "containers":[{"id":"l1","label":"L1"},{"id":"l2","label":"L2"}],
         "nodes":[{"id":"a","label":"A","containerId":"l1"},
                  {"id":"b","label":"B","containerId":"l2"}],
         "edges":[{"id":"e","from":"a","to":"b"}]}
        """;

        Run(contained, """{"specVersion":"1.0","layout":"swimlane"}""", out var inferred);
        Run(contained, """{"specVersion":"1.0","layout":"swimlane","direction":"topToBottom"}""", out var stated);

        var inferredY = inferred.Pages[0].Groups.Select(g => Math.Round(g.Bounds!.Y, 2)).OrderBy(v => v).ToList();
        var statedY = stated.Pages[0].Groups.Select(g => Math.Round(g.Bounds!.Y, 2)).OrderBy(v => v).ToList();

        Assert.Equal(inferredY, statedY);
    }

    [Fact]
    public void AnAnchorDoesNotNeedAnExplicitMode()
    {
        // I recorded the opposite once; `IrAnchor.Mode` defaults to "fixed".
        Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "nodes":[{"id":"a","label":"A","anchor":{"x":3,"y":7}}],"edges":[]}
        """, """{"specVersion":"1.0","layout":"manual"}""", out var diagram);

        var node = diagram.Pages[0].Nodes.Single();

        Assert.Equal(3, node.Bounds!.X, 1);
        Assert.Equal(7, node.Bounds.Y, 1);
    }
}
