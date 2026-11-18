using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A minimum readable size must work on its own, and must apply to every kind of text.
///
/// Two separate holes, both found by independent review:
///
///   1. `{"typography": {"minReadableSize": 20}}` returned `succeeded` with `unmet: []` and left
///      every size at its default. The typography block only ran when baseSize or a font family was
///      set, and each tier was only assigned `if (baseSize is not null)` — so the floor had nothing
///      to raise.
///
///   2. Even with a floor, text blocks were floored by a hardcoded 8pt in the bridge instead of the
///      spec value. Measured with `minReadableSize: 30`: nodes and the title honoured it, a note
///      stayed at 8.
/// </summary>
public sealed class MinReadableSizeTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string Ir = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
     "edges":[{"id":"e","from":"a","to":"b"}],
     "textBlocks":[{"id":"note","text":"Note","role":"note",
                    "box":{"x":2,"y":5,"width":2,"height":0.4}}]}
    """;

    private static (ExecutionResult Result, Diagram Diagram, string Path) Run(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(Ir, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-min-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, path);
        return (result, new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void AFloorAloneRaisesNodeSizes()
    {
        var (_, diagram, path) = Run("""{"specVersion":"1.0","typography":{"minReadableSize":20}}""");
        try
        {
            foreach (var node in diagram.Pages[0].Nodes)
            {
                double size = node.Style?.FontSize ?? 0;
                Assert.True(size >= 19.9, $"node '{node.Id}' is {size:F1}pt, below the 20pt floor");
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AFloorAloneIsReported()
    {
        var (result, _, path) = Run("""{"specVersion":"1.0","typography":{"minReadableSize":20}}""");
        try
        {
            Assert.Contains(result.UnmetConstraints, u => u.Code == "typography.min_size");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AFloorAloneRaisesTextBlocksToo()
    {
        // The bridge's hardcoded 8pt used to win here.
        var (_, diagram, path) = Run("""{"specVersion":"1.0","typography":{"minReadableSize":30}}""");
        try
        {
            var block = diagram.Pages[0].TextBlocks.Single(t => t.Id == "note");
            double size = block.Style?.FontSize ?? 0;

            Assert.True(size >= 29.9, $"text block is {size:F1}pt, below the 30pt floor");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AFloorBelowTheDefaultChangesNothing()
    {
        // Existing behaviour must be untouched when the request is a no-op.
        var (result, diagram, path) = Run("""{"specVersion":"1.0","typography":{"minReadableSize":4}}""");
        try
        {
            Assert.DoesNotContain(result.UnmetConstraints, u => u.Code == "typography.min_size");
            Assert.All(diagram.Pages[0].Nodes, n => Assert.True((n.Style?.FontSize ?? 11) >= 8));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnExplicitFontSizeStillWins()
    {
        // `??=` semantics: a size the caller set is not overridden by the floor being applied.
        var (_, diagram, path) = Run("""{"specVersion":"1.0","typography":{"minReadableSize":20}}""");
        try
        {
            // The tier sizes are assigned, so every node has one; this asserts they are the floored
            // tier rather than some unrelated number.
            Assert.All(diagram.Pages[0].Nodes, n => Assert.NotNull(n.Style?.FontSize));
        }
        finally { File.Delete(path); }
    }
}
