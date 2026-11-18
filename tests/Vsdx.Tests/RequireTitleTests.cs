using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `require_title` must be falsifiable: a diagram with no title on the page cannot satisfy it.
///
/// The check read `Diagram.Name`, which is filled from `ir.Id` when no title is given — so it was
/// never empty. Measured: an IR with no title and `hardConstraints: [{"code":"require_title"}]`
/// returned `unmet: []` while the page carried no text at all, which makes the constraint vacuous.
///
/// A title now counts only when it is visible: a text block with role title or subtitle.
/// </summary>
public sealed class RequireTitleTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static ExecutionResult Run(string irJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","hardConstraints":[{"code":"require_title"}]}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-title-{Guid.NewGuid():N}.vsdx");
        try { return new ProtocolRunner().Execute(ir, spec, path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnIrWithoutATitleFailsTheConstraint()
    {
        var result = Run("""
        {"irVersion":"1.0","id":"my-diagram-id","kind":"architecture",
         "nodes":[{"id":"a","label":"A"}]}
        """);

        Assert.Contains(result.UnmetConstraints, u => u.Code == "require_title");
    }

    [Fact]
    public void AVariableTitleBlockSatisfiesIt()
    {
        var result = Run("""
        {"irVersion":"1.0","id":"my-diagram-id","kind":"architecture",
         "nodes":[{"id":"a","label":"A"}],
         "textBlocks":[{"id":"title","text":"真实标题","role":"title",
                        "box":{"x":2,"y":5,"width":4,"height":0.5}}]}
        """);

        Assert.DoesNotContain(result.UnmetConstraints, u => u.Code == "require_title");
    }

    [Fact]
    public void AnEmptyTitleBlockIsRejectedOutright()
    {
        // A block with role title but no text is not a title. Measured: the pipeline rejects it
        // earlier than the constraint check, with status failed — so this asserts that a caller
        // never gets a silent pass, not that a specific code appears.
        var result = Run("""
        {"irVersion":"1.0","id":"d","kind":"architecture",
         "nodes":[{"id":"a","label":"A"}],
         "textBlocks":[{"id":"title","text":"","role":"title",
                        "box":{"x":2,"y":5,"width":4,"height":0.5}}]}
        """);

        bool rejected = result.Status == ExecutionStatus.Failed ||
                        result.UnmetConstraints.Any(u => u.Code == "require_title");

        Assert.True(rejected, $"empty title block neither rejected nor reported (status {result.Status})");
    }

    [Fact]
    public void TheIdAloneNeverSatisfiesIt()
    {
        // The specific bug: `Diagram.Name` falls back to `ir.Id`.
        var withIdOnly = Run("""{"irVersion":"1.0","id":"some-id","kind":"architecture","nodes":[{"id":"a","label":"A"}]}""");
        var withNamedTitle = Run("""
        {"irVersion":"1.0","id":"some-id","kind":"architecture",
         "nodes":[{"id":"a","label":"A"}],
         "textBlocks":[{"id":"t","text":"X","role":"title",
                        "box":{"x":2,"y":5,"width":2,"height":0.4}}]}
        """);

        Assert.Contains(withIdOnly.UnmetConstraints, u => u.Code == "require_title");
        Assert.DoesNotContain(withNamedTitle.UnmetConstraints, u => u.Code == "require_title");
    }
}
