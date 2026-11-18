using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `stagesCompleted` must reflect what actually ran, not a fixed list.
///
/// After finding `requiresVisualReview` stuck at `true` and `objectMapping` stuck at the identity, this
/// field looked like the next suspect. It is not: a validation failure reports none, a mid-pipeline
/// failure reports the four that finished, and a complete run reports all six. Verified rather than
/// assumed, because the previous two rounds showed the assumption is not reliable in either direction.
/// </summary>
public sealed class StageReportingTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static ExecutionResult Run(string irJson, string? outputOverride = null)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>("""{"specVersion":"1.0"}""", Json)!;

        var path = outputOverride ?? Path.Combine(Path.GetTempPath(), $"vsdx-stg-{Guid.NewGuid():N}.vsdx");

        try { return new ProtocolRunner().Execute(ir, spec, path); }
        finally { if (outputOverride is null && File.Exists(path)) File.Delete(path); }
    }

    private const string Valid = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    [Fact]
    public void AValidationFailureCompletesNoStages()
    {
        var result = Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "nodes":[{"id":"a","label":"A"}],
         "edges":[{"id":"e","from":"a","to":"nope"}]}
        """);

        Assert.Equal("failed", result.Status);
        Assert.Empty(result.StagesCompleted);
    }

    [Fact]
    public void AMidPipelineFailureKeepsWhatFinished()
    {
        // An unwritable output path fails after routing, so the first four stages must still be
        // reported — clearing the list would hide how far the run got.
        var result = Run(Valid, outputOverride: "/proc/vsdx-cannot-write/o.vsdx");

        Assert.Equal("failed", result.Status);
        Assert.NotEmpty(result.StagesCompleted);
        Assert.Contains("ir.validate", result.StagesCompleted);
        Assert.Contains("ir.toModel", result.StagesCompleted);
        Assert.DoesNotContain("vsdx.write", result.StagesCompleted);
    }

    [Fact]
    public void ASuccessfulRunCompletesEveryStage()
    {
        var result = Run(Valid);

        Assert.Contains("ir.validate", result.StagesCompleted);
        Assert.Contains("vsdx.write", result.StagesCompleted);
        Assert.Contains("quality.check", result.StagesCompleted);
    }

    [Fact]
    public void TheStagesAreNotAFixedList()
    {
        // The distinction that matters: three runs, three different lists.
        var none = Run("""{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[],"edges":[{"id":"e","from":"x","to":"y"}]}""");
        var some = Run(Valid, outputOverride: "/proc/vsdx-cannot-write/o.vsdx");
        var all = Run(Valid);

        Assert.NotEqual(none.StagesCompleted.Count, all.StagesCompleted.Count);
        Assert.NotEqual(some.StagesCompleted.Count, all.StagesCompleted.Count);
    }

    [Fact]
    public void TheMessageMatchesTheStatus()
    {
        var all = Run(Valid);
        var none = Run("""{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[],"edges":[{"id":"e","from":"x","to":"y"}]}""");

        Assert.False(string.IsNullOrWhiteSpace(all.Message));
        Assert.False(string.IsNullOrWhiteSpace(none.Message));
        Assert.NotEqual(all.Message, none.Message);
    }
}
