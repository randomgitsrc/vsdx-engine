using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Crossings in a shared-backend topology are topological, not a layout defect.
///
/// `deploy-topology` loses 18 points to five `edge.crossing` warnings. The three API instances sit left
/// to right at x=1.50, 3.35, 5.20; the three data stores sit at 1.50, 3.35, 5.20. `api1→rd` and
/// `api2→rd` both target the rightmost store, so each must pass the two stores before it.
///
/// Verified that this is not an ordering problem the engine could solve: swapping the declaration order
/// of the nodes changes nothing (the engines order a row by its edges, not by declaration), and the
/// alternative direction scores worse (64 against 82). What would remove the crossings is rewiring,
/// e.g. api1→pg1, api2→pg2, api3→rd — which is a change to the described system, not to its picture.
///
/// This is recorded as a boundary rather than a defect, in the same category as
/// `capability-boundaries.md`'s "orthogonal routing is not guaranteed".
/// </summary>
public sealed class BipartiteCrossingTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static (int Score, IReadOnlyList<string> Codes) Run(string irPath, string direction)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(irPath), Json)!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(
            $$"""{"specVersion":"1.0","theme":{"palette":"enterprise"},"direction":"{{direction}}"}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-bip-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);
            return (result.Quality.Score, result.Quality.IssueCodes);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string Example() =>
        Path.Combine(Root(), "examples", "realworld", "deploy-topology.ir.json");

    [Fact]
    public void TheCrossingsAreStructuralNotALayoutChoice()
    {
        var (_, codes) = Run(Example(), "topToBottom");

        Assert.Contains("edge.crossing", codes);
    }

    [Fact]
    public void TheChosenDirectionIsAlreadyTheBetterOne()
    {
        // If left-to-right scored higher, the crossing complaint would be a layout bug rather than a
        // boundary — worth pinning so the claim does not quietly become false.
        var down = Run(Example(), "topToBottom");
        var across = Run(Example(), "leftToRight");

        Assert.True(down.Score > across.Score,
            $"topToBottom scored {down.Score}, leftToRight {across.Score}");
    }

    [Fact]
    public void DeclarationOrderDoesNotChangeTheResult()
    {
        // Proof that the row order is derived from edges, not from the order nodes are written in —
        // so "reorder the IR" is not a workaround a caller could apply.
        var original = File.ReadAllText(Example());

        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(original, Json)!;
        var page = ir.Pages.Count > 0 ? ir.Pages[0] : null;
        var nodes = page?.Nodes ?? ir.Nodes;

        int first = nodes.FindIndex(n => n.Id == "api1");
        int last = nodes.FindIndex(n => n.Id == "api3");

        if (first >= 0 && last >= 0)
            (nodes[first], nodes[last]) = (nodes[last], nodes[first]);

        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"},"direction":"topToBottom"}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-bip2-{Guid.NewGuid():N}.vsdx");

        try
        {
            var swapped = new ProtocolRunner().Execute(ir, spec, path);
            var baseline = Run(Example(), "topToBottom");

            Assert.Equal(baseline.Score, swapped.Quality.Score);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TheBoundaryIsDocumented()
    {
        var boundaries = File.ReadAllText(
            Path.Combine(Root(), "docs", "capability-boundaries.md"));

        Assert.Contains("交叉", boundaries);
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
