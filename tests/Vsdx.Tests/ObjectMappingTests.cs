using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `objectMapping` maps an IR id to the engine's model id — and those are always the same string.
///
/// The doc comment says it lets a caller "map results back to their input", which reads as though the
/// two could differ. They cannot: `IrNode.Id` becomes `Node.Id` verbatim, so the dictionary is the
/// identity on every object it contains. Measured on the cross-border diagram: 22 entries, 0 of them
/// non-identity.
///
/// This is not a defect to fix by changing the mapping — it is a field whose current content is
/// trivial and whose name invites the wrong reading. The tests below pin what it actually contains so
/// a caller cannot mistake it for a lookup from model id to the integer Shape ID inside the package.
/// </summary>
public sealed class ObjectMappingTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static ExecutionResult Run(string irJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-map-{Guid.NewGuid():N}.vsdx");
        try { return new ProtocolRunner().Execute(ir, spec, path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void EveryEntryIsTheIdentity()
    {
        var result = Run(File.ReadAllText(
            Path.Combine(Root(), "examples", "realworld", "cbo.ir.json")));

        Assert.NotEmpty(result.ObjectMapping);

        var nonIdentity = result.ObjectMapping.Where(kv => !string.Equals(kv.Key, kv.Value, StringComparison.Ordinal)).ToList();

        Assert.True(nonIdentity.Count == 0,
            $"non-identity entries: {string.Join(", ", nonIdentity.Select(kv => $"{kv.Key}->{kv.Value}"))}");
    }

    [Fact]
    public void ItCoversEveryObjectKind()
    {
        // Nodes, containers, edges, text blocks and paths are all included, so a caller can rely on
        // the dictionary being complete even though its values carry no new information.
        var result = Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "containers":[{"id":"g","label":"G"}],
         "nodes":[{"id":"a","label":"A","containerId":"g"}],
         "edges":[],
         "textBlocks":[{"id":"note","text":"N","role":"note","box":{"x":4,"y":2,"width":2,"height":0.4}}]}
        """);

        Assert.Contains("g", result.ObjectMapping.Keys);
        Assert.Contains("a", result.ObjectMapping.Keys);
        Assert.Contains("note", result.ObjectMapping.Keys);
    }

    [Fact]
    public void ItDoesNotContainTheIntegerShapeIdsFromThePackage()
    {
        // The trap: a caller searching for the package's Shape ID will not find it here.
        var result = Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
         "edges":[{"id":"e","from":"a","to":"b"}]}
        """);

        Assert.All(result.ObjectMapping.Values, v => Assert.False(int.TryParse(v, out _)));
    }

    [Fact]
    public void TheDocumentedMeaningMatchesWhatItContains()
    {
        var source = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Protocol", "ExecutionResult.cs"));

        int at = source.IndexOf("public Dictionary<string, string> ObjectMapping", StringComparison.Ordinal);
        Assert.True(at > 0);

        // The comment above the property must say the ids currently coincide, or a reader assumes a
        // translation is happening.
        var comment = source[Math.Max(0, at - 900)..at];

        Assert.Contains("identity", comment, StringComparison.OrdinalIgnoreCase);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
