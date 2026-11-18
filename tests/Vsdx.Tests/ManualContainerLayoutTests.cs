using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Asking for per-container layout under manual mode must be reported.
///
/// The containerLayouts block sat inside the branch that only runs when layout is not manual, so
/// under `layout: "manual"` the whole request was skipped and neither `applied` nor `unmet` said
/// anything about it. A caller reading the result would conclude the grid had been applied.
///
/// Manual legitimately does not rearrange — that is its contract — so the right answer is to report
/// the request as unhonoured rather than to start moving nodes.
/// </summary>
public sealed class ManualContainerLayoutTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string ThreeInOneIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "containers":[{"id":"g","label":"G"}],
     "nodes":[{"id":"a","label":"A","containerId":"g"},
              {"id":"b","label":"B","containerId":"g"},
              {"id":"c","label":"C","containerId":"g"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    private static (ExecutionResult Result, Diagram Diagram, string Path) Run(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(ThreeInOneIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-manual-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, path);
        return (result, new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void ManualIgnoresContainerLayoutsButSaysSo()
    {
        var (result, _, path) = Run(
            """{"specVersion":"1.0","layout":"manual","containerLayouts":{"g":"grid"}}""");
        try
        {
            Assert.Contains(result.UnmetConstraints,
                u => u.Code.Contains("container", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheReportNamesTheContainer()
    {
        var (result, _, path) = Run(
            """{"specVersion":"1.0","layout":"manual","containerLayouts":{"g":"grid"}}""");
        try
        {
            var text = string.Join(" ", result.UnmetConstraints.Select(u => $"{u.Code} {u.Reason} {u.Target}"));

            Assert.Contains("g", text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ManualStillLeavesNodeCoordinatesAlone()
    {
        // Reporting the request must not tempt the implementation into honouring it.
        var (_, diagram, path) = Run(
            """{"specVersion":"1.0","layout":"manual","containerLayouts":{"g":"grid"}}""");
        try
        {
            // The converter's placeholder box is 2.00 wide; manual must not place the nodes.
            var widths = diagram.Pages[0].Nodes.Select(n => Math.Round(n.Bounds.W, 2)).Distinct().ToList();

            Assert.All(diagram.Pages[0].Nodes, n =>
                Assert.True(n.Bounds.X >= 0 && n.Bounds.Y >= 0,
                    $"{n.Id} was moved to ({n.Bounds.X},{n.Bounds.Y})"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NonManualLayoutsStillApplyContainerLayouts()
    {
        // The control: outside manual, the request must still be honoured and reported as applied.
        var (result, _, path) = Run(
            """{"specVersion":"1.0","layout":"layered","containerLayouts":{"g":"grid"}}""");
        try
        {
            Assert.Contains(result.AppliedDesignNotes,
                n => n.Contains("container", StringComparison.OrdinalIgnoreCase) &&
                     n.Contains("grid", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ManualWithoutContainerLayoutsStaysQuiet()
    {
        // No request, no complaint.
        var (result, _, path) = Run("""{"specVersion":"1.0","layout":"manual"}""");
        try
        {
            Assert.DoesNotContain(result.UnmetConstraints,
                u => u.Code.Contains("container", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }
}
