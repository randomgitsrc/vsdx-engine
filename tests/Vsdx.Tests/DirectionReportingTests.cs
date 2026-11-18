using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `applied` must name the direction that was used, not merely that one was inferred.
///
/// Reporting `direction=auto` leaves a caller unable to tell whether their diagram came out portrait
/// or landscape without opening the file — and the two scenarios differ: a diagram with containers
/// reads top to bottom, a container-less flow reads left to right.
/// </summary>
public sealed class DirectionReportingTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string ContainedIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "containers":[{"id":"band","label":"Band"}],
     "nodes":[{"id":"a","label":"A","containerId":"band"},
              {"id":"b","label":"B","containerId":"band"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    private const string FlatIr = """
    {"irVersion":"1.0","id":"p","kind":"workflow",
     "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    private static ExecutionResult Run(string irJson, string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-dir-{Guid.NewGuid():N}.vsdx");
        try { return new ProtocolRunner().Execute(ir, spec, path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnInferredDirectionIsNamed()
    {
        var result = Run(ContainedIr, """{"specVersion":"1.0"}""");

        var note = result.AppliedDesignNotes.First(n => n.Contains("direction=", StringComparison.Ordinal));

        Assert.Contains("topToBottom", note);
        Assert.Contains("auto", note);
    }

    [Fact]
    public void AContainerlessFlowReportsLandscape()
    {
        var result = Run(FlatIr, """{"specVersion":"1.0"}""");

        var note = result.AppliedDesignNotes.First(n => n.Contains("direction=", StringComparison.Ordinal));

        Assert.Contains("leftToRight", note);
    }

    [Fact]
    public void AStatedDirectionIsReportedWithoutTheAutoMarker()
    {
        var result = Run(ContainedIr, """{"specVersion":"1.0","direction":"leftToRight"}""");

        var note = result.AppliedDesignNotes.First(n => n.Contains("direction=", StringComparison.Ordinal));

        Assert.Contains("leftToRight", note);
        Assert.DoesNotContain("auto", note);
    }
}
