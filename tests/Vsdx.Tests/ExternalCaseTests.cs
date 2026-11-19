using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Protocol;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The first example in this repository that was not invented here.
///
/// Every other scenario in `examples/realworld` was designed by the person who wrote the engine, so they test
/// "can it draw what I had in mind". This one is transcribed from Netflix's published active-active architecture —
/// components, regions and the region-autonomy constraint all come from the source, not from a sketch.
///
/// It found something the invented cases could not: two structurally identical regions come out with their members
/// in **different orders**, so a reader cannot compare them row by row. `OrderWithinBands` walks the graph within
/// each band and has no notion of "these two containers are the same shape, order them the same way".
/// </summary>
public sealed class ExternalCaseTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static Diagram Load()
    {
        var path = Path.Combine(Root(), "examples", "realworld", "netflix-active-active.ir.json");

        var ir = JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return DiagramIrConverter.ToDiagram(ir);
    }

    [Fact]
    public void TheCaseCarriesItsProvenance()
    {
        // An example without a source is a sketch. This one has to say where it came from, or it is no better than
        // the six that were made up.
        var path = Path.Combine(Root(), "examples", "realworld", "netflix-active-active.ir.json");
        var ir = JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.False(string.IsNullOrWhiteSpace(ir.Metadata?.Source));
        Assert.Contains("netflix", ir.Metadata!.Source!, StringComparison.OrdinalIgnoreCase);

        // `IrMetadata` has no URL field; the link lives in `Tags`, which is the only free-form slot it has.
        Assert.True(ir.Metadata.Tags?.ContainsKey("sourceUrl") == true,
            "an external case without a link cannot be checked");
    }

    [Fact]
    public void ItIsDeliverable()
    {
        var diagram = Load();
        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","layout":"layered","direction":"topToBottom","theme":{"palette":"enterprise"}}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-ext-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ReadIr(), spec, output);

            Assert.True(result.Quality.HardConstraintsPassed,
                "hard failures: " + string.Join(", ", result.Quality.IssueCodes));
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public void TwoSymmetricRegionsOrderTheirMembersIdentically()
    {
        // This started as a recorded defect and turned out to be my modelling. The first version of the IR gave
        // `east` an edge `disc → evc` and omitted the matching one for `west`, so `evc-w` had no in-band
        // predecessor, floated free, and sorted ahead of the chain. The engine was reading the graph correctly —
        // two regions that are not the same graph do not get the same order.
        //
        // With the missing edge added they agree. The test now asserts the symmetry the example claims.
        var diagram = Load();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        static List<string> Order(Page page, string group) => page.Nodes
            .Where(n => n.GroupId == group)
            .OrderBy(n => n.Bounds.X)
            .Select(n => n.Text ?? n.Id)
            .ToList();

        var east = Order(diagram.Pages[0], "east");
        var west = Order(diagram.Pages[0], "west");

        Assert.Equal(east.Count, west.Count);

        // Same shape, same order — what a reader comparing the two regions needs.
        Assert.Equal(
            east.Select(t => t.Replace("-e", "", StringComparison.Ordinal)).ToList(),
            west.Select(t => t.Replace("-w", "", StringComparison.Ordinal)).ToList());
    }

    /// <summary>Reads the IR again, since the runner converts it itself.</summary>
    private static DiagramIr ReadIr() => JsonSerializer.Deserialize<DiagramIr>(
        File.ReadAllText(Path.Combine(Root(), "examples", "realworld", "netflix-active-active.ir.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}
