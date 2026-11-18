using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A constraint's `note` is the caller's explanation of why the constraint matters. It must reach the
/// report, not be accepted and dropped.
///
/// `ConstraintSpec` has `Code`, `Value`, `Target` and `Note`. The first three drive the check; `Note` is
/// read by nothing — the schema allows it, the deserialiser accepts it, and no output mentions it. A caller
/// who writes `{"code":"require_title","note":"交付前必须补标题"}` gets a verdict with their reasoning
/// removed, which reads as though the engine never received it.
/// </summary>
public sealed class ConstraintNoteTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string Ir = """
    {"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A"}],"edges":[]}
    """;

    private static ExecutionResult Run(string specJson, string? irJson = null)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(irJson ?? Ir, Json)!;
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-note-{Guid.NewGuid():N}.vsdx");

        try { return new ProtocolRunner().Execute(ir, spec, path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AnUnsatisfiedConstraintCarriesItsNote()
    {
        var result = Run("""
        {"specVersion":"1.0","hardConstraints":[
          {"code":"require_title","note":"交付前必须补标题"}]}
        """);

        var unmet = result.UnmetConstraints.Single(u => u.Code == "require_title");

        Assert.Contains("交付前必须补标题", unmet.Reason);
    }

    [Fact]
    public void ASatisfiedConstraintProducesNoEntryAtAll()
    {
        // The note explains why the constraint exists; a satisfied check reports nothing, note included.
        var result = Run(
            """{"specVersion":"1.0","hardConstraints":[{"code":"require_title","note":"备注"}]}""",
            """
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "nodes":[{"id":"a","label":"A"}],"edges":[],
             "textBlocks":[{"id":"title","text":"真实标题","role":"title",
                            "box":{"x":3,"y":6,"width":4,"height":0.5}}]}
            """);

        Assert.DoesNotContain(result.UnmetConstraints, u => u.Code == "require_title");
    }

    [Fact]
    public void AConstraintWithoutANoteReportsAsBefore()
    {
        // The negative case: no note must not produce an empty bracket or stray punctuation.
        var result = Run("""
        {"specVersion":"1.0","hardConstraints":[{"code":"require_title"}]}
        """);

        var unmet = result.UnmetConstraints.Single(u => u.Code == "require_title");

        Assert.DoesNotContain("()", unmet.Reason);
        Assert.DoesNotContain("：null", unmet.Reason);
    }
}
