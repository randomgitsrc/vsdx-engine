using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// An unrecognised enum value must be reported, not quietly replaced by a default.
///
/// Every parse site fell back to a default: `layout: "kaboom"` ran a layered pass and said
/// nothing, `direction: "sideways"` became left-to-right. Worst of the four,
/// `containerLayouts: {"g1": "kaboom"}` reported `applied: 'container g1 relaid as kaboom'` —
/// claiming to have executed a strategy that does not exist.
///
/// Falling back is reasonable; reporting a different value than the one requested is not. The
/// parse therefore returns whether the value was recognised, and callers pass that on.
/// </summary>
public sealed class EnumReportingTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string GridIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "containers":[{"id":"g1","label":"L"}],
     "nodes":[{"id":"a","label":"A","containerId":"g1"},{"id":"b","label":"B","containerId":"g1"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    private static ExecutionResult Run(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(GridIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-enum-{Guid.NewGuid():N}.vsdx");
        try
        {
            return new ProtocolRunner().Execute(ir, spec, path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static IEnumerable<string> Codes(ExecutionResult r) =>
        r.UnmetConstraints.Select(u => u.Code);

    // ---- Parse-level ---------------------------------------------------------

    [Fact]
    public void ParseReportsWhetherTheValueWasRecognised()
    {
        Assert.True(DiagramLayoutEngine.TryParse("layered", out _));
        Assert.True(DiagramLayoutEngine.TryParse("GRID", out var grid));
        Assert.Equal(LayoutStrategy.Grid, grid);

        Assert.False(DiagramLayoutEngine.TryParse("kaboom", out _));
        Assert.False(DiagramLayoutEngine.TryParse("", out _));
    }

    [Fact]
    public void ParseStillReturnsAUsableStrategyOnFailure()
    {
        // Returns false *and* hands back something runnable. The caller needs both: a strategy to
        // use, and the knowledge that it is not the one they asked for. Throwing would leave
        // nothing to run with.
        Assert.False(DiagramLayoutEngine.TryParse("kaboom", out var fallback));
        Assert.Equal(LayoutStrategy.Layered, fallback);
    }

    // ---- Spec-level ----------------------------------------------------------

    [Fact]
    public void UnknownLayoutIsReported()
    {
        var result = Run("""{"specVersion":"1.0","layout":"kaboom"}""");

        Assert.Contains(Codes(result), c => c.Contains("layout", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownContainerLayoutIsReportedAndNotClaimedAsApplied()
    {
        var result = Run("""{"specVersion":"1.0","containerLayouts":{"g1":"kaboom"}}""");

        Assert.Contains(Codes(result), c => c.Contains("container", StringComparison.OrdinalIgnoreCase));

        // The damaging part: it used to claim the bogus strategy had been executed.
        Assert.DoesNotContain(result.AppliedDesignNotes,
            n => n.Contains("kaboom", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownDirectionIsReported()
    {
        var result = Run("""{"specVersion":"1.0","direction":"sideways"}""");

        Assert.Contains(Codes(result), c => c.Contains("direction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownCanvasModeIsReported()
    {
        // This one already reported; the test keeps it that way.
        var result = Run("""{"specVersion":"1.0","canvas":{"mode":"kaboom","width":5,"height":4}}""");

        Assert.Contains(Codes(result), c => c.Contains("canvas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void KnownValuesAreNotReported()
    {
        // A warning that fires on correct input is noise and stops being read.
        var result = Run(
            """{"specVersion":"1.0","layout":"grid","direction":"topToBottom","canvas":{"mode":"auto"}}""");

        Assert.DoesNotContain(Codes(result), c => c.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheReportNamesTheRejectedValue()
    {
        var result = Run("""{"specVersion":"1.0","layout":"kaboom"}""");

        var text = string.Join(" ", result.UnmetConstraints.Select(u => $"{u.Code} {u.Reason}"));

        Assert.Contains("kaboom", text, StringComparison.OrdinalIgnoreCase);
    }
}
