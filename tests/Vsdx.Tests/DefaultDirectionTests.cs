using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A layered diagram should default to the orientation it reads best in.
///
/// C15 made `leftToRight` genuinely transpose, which also changed what the *default* does: every
/// spec that omitted `direction` started producing a rotated sheet. Measured on three real
/// diagrams, layered structures lose 6-18 points that way while a container-less flow gains:
///
///   cbo          95 -> 88
///   deploy       94 -> 76
///   data flow    93 -> 96
///
/// Layering is a vertical idea — bands stack, nodes sit inside them. Turning it sideways spreads
/// each band along the page and multiplies the crossings. A container-less flow has no bands to
/// stack, so horizontal is what it wants.
/// </summary>
public sealed class DefaultDirectionTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string LayeredIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "containers":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
     "nodes":[{"id":"n1","label":"N1","containerId":"a"},
              {"id":"n2","label":"N2","containerId":"b"}],
     "edges":[{"id":"e","from":"n1","to":"n2"}]}
    """;

    private const string FlatIr = """
    {"irVersion":"1.0","id":"p","kind":"workflow",
     "nodes":[{"id":"n1","label":"N1"},{"id":"n2","label":"N2"},{"id":"n3","label":"N3"}],
     "edges":[{"id":"e1","from":"n1","to":"n2"},{"id":"e2","from":"n2","to":"n3"}]}
    """;

    private static Diagram Run(string irJson, string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-dir-{Guid.NewGuid():N}.vsdx");
        new ProtocolRunner().Execute(ir, spec, path);

        try { return new VsdxEngine().Read(path).Diagram; }
        finally { File.Delete(path); }
    }

    private static bool IsWide(Diagram d)
    {
        var page = d.Pages[0];
        var xs = page.Nodes.Select(n => n.Bounds.X).ToList();
        var ys = page.Nodes.Select(n => n.Bounds.Y).ToList();

        return xs.Max() - xs.Min() > ys.Max() - ys.Min();
    }

    [Fact]
    public void AContainedDiagramDefaultsToPortrait()
    {
        // Bands stack; turning them sideways pulls each band across the page.
        var diagram = Run(LayeredIr, """{"specVersion":"1.0"}""");

        Assert.False(IsWide(diagram), "a layered diagram should read top to bottom by default");
    }

    [Fact]
    public void AContainerlessFlowDefaultsToLandscape()
    {
        var diagram = Run(FlatIr, """{"specVersion":"1.0"}""");

        Assert.True(IsWide(diagram), "a container-less flow should read left to right by default");
    }

    [Fact]
    public void AnExplicitDirectionStillWins()
    {
        // The default is a default: stating a direction must override it in either structure.
        Assert.True(IsWide(Run(LayeredIr, """{"specVersion":"1.0","direction":"leftToRight"}""")));
        Assert.False(IsWide(Run(FlatIr, """{"specVersion":"1.0","direction":"topToBottom"}""")));
    }
}
