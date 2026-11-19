using Vsdx.Engine;
using Vsdx.Protocol;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `IrEdge.Relation` documents eight semantics and says they drive "line style and arrow defaults". Only the arrow
/// half was implemented: `publishes`, `consumes` and every other value produced identical geometry.
///
/// A diagram where an asynchronous event and a synchronous call look the same has lost the distinction that
/// matters most in a service architecture — the visual reviewer put this first among the things worth fixing,
/// because line style is what lets a reader tell "waits for an answer" from "fires and forgets".
///
/// The convention here is the one C4 and most architecture notations use: solid for a synchronous call, dashed for
/// an asynchronous message.
/// </summary>
public sealed class RelationLineStyleTests
{
    [Theory]
    [InlineData("publishes")]
    [InlineData("consumes")]
    public void AsynchronousRelationsAreDashed(string relation)
    {
        var edge = Convert(relation);

        Assert.NotNull(edge.LinePattern);
        Assert.NotEqual(1, edge.LinePattern);          // 1 is solid in Visio
    }

    [Theory]
    [InlineData("calls")]
    [InlineData("reads")]
    [InlineData("writes")]
    [InlineData("flows")]
    [InlineData("depends")]
    [InlineData("contains")]
    [InlineData(null)]
    public void SynchronousRelationsStaySolid(string? relation)
    {
        var edge = Convert(relation);

        // Solid is the default, so either an explicit 1 or an absent pattern is fine — but not dashed.
        Assert.True(edge.LinePattern is null or 1,
            $"{relation ?? "(none)"} produced LinePattern={edge.LinePattern}, expected solid");
    }

    [Fact]
    public void ThePatternReachesThePackage()
    {
        // A style that only exists in the model is not a feature.
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            """
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
             "edges":[{"id":"e","from":"a","to":"b","relation":"publishes"}]}
            """,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var diagram = DiagramIrConverter.ToDiagram(ir);
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-dash-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(diagram, output, new WriteOptions { AutoLayout = true });

            using var zip = System.IO.Compression.ZipFile.OpenRead(output);
            using var reader = new StreamReader(zip.Entries
                .First(e => e.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)
                         && e.FullName != "visio/pages/pages.xml").Open());
            var xml = reader.ReadToEnd();

            Assert.Contains("LinePattern", xml);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    private static Edge Convert(string? relation)
    {
        var json = relation is null
            ? """{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],"edges":[{"id":"e","from":"a","to":"b"}]}"""
            : $$"""{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],"edges":[{"id":"e","from":"a","to":"b","relation":"{{relation}}"}]}""";

        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return DiagramIrConverter.ToDiagram(ir).Pages[0].Edges[0];
    }
}
