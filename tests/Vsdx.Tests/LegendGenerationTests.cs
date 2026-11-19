using Vsdx.Engine;
using Vsdx.Protocol;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A diagram with five colours, four shapes and three bands, and nothing saying what any of them mean.
///
/// The C4 checklist has "有图例/键" as one line, and both reference diagrams fail it — the visual reviewer listed
/// it under "显著" because the reader has to reverse-engineer the encoding. The knowledge base has carried the
/// checklist for a while without the engine being able to satisfy it.
///
/// The information needed is already present and structured: `Palettes` maps role to colour, `ShapeForRole` maps
/// role to master, and the IR knows which roles the diagram actually uses. So the legend should be *derived*, not
/// hand-drawn — and it must only list roles the diagram uses, or it becomes a template rather than a description.
/// </summary>
public sealed class LegendGenerationTests
{
    private static Diagram TwoRoles()
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            """
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "nodes":[
              {"id":"web","label":"Web","role":"client"},
              {"id":"svc","label":"服务","role":"service"},
              {"id":"db","label":"库","role":"database"}],
             "edges":[]}
            """,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return DiagramIrConverter.ToDiagram(ir);
    }

    [Fact]
    public void TheLegendListsOnlyTheRolesTheDiagramUses()
    {
        var diagram = TwoRoles();

        var entries = LegendBuilder.Entries(diagram, "enterprise");

        var roles = entries.Select(e => e.Role).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("client", roles);
        Assert.Contains("service", roles);
        Assert.Contains("database", roles);

        // Roles that appear in the palette but not in this diagram must not be listed.
        Assert.DoesNotContain("actor", roles);
        Assert.DoesNotContain("monitor", roles);
    }

    [Fact]
    public void EachEntryCarriesTheColourAndShapeThatWillBeDrawn()
    {
        var entries = LegendBuilder.Entries(TwoRoles(), "enterprise");

        var client = entries.Single(e => e.Role.Equals("client", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("#2E75B6", client.Fill, ignoreCase: true);
        Assert.Equal("Rectangle", client.Shape);
    }

    [Fact]
    public void ShapingIsNotIncludedWhenItWouldNotHelp()
    {
        // A legend that restates the obvious adds noise. When every role maps to the same master, listing the
        // shape per row says nothing — the row should carry colour and meaning only.
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            """
            {"irVersion":"1.0","id":"t","kind":"architecture",
             "nodes":[{"id":"a","label":"A","role":"service"},{"id":"b","label":"B","role":"region"}],
             "edges":[]}
            """,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var entries = LegendBuilder.Entries(DiagramIrConverter.ToDiagram(ir), "enterprise");

        Assert.All(entries, e => Assert.Equal("Rectangle", e.Shape));
    }

    [Fact]
    public void TheLegendSitsBelowTheContainersNotJustTheNodes()
    {
        // A band extends below its members. Anchoring to the nodes put the legend across the bottom band's
        // border — measured, three labels straddling it and a 100-point diagram scoring 88.
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };
        page.Groups.Add(new Group { Id = "band", Name = "数据层", Kind = "layer", Order = 1, Members = { "a" } });
        page.Nodes.Add(new Node { Id = "a", Text = "A", Type = "database", GroupId = "band", Bounds = new Bounds(5, 3, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Type = "service", Bounds = new Bounds(5, 7, 1.4, 0.6) });

        var diagram = new Diagram { Id = "t", Type = "architecture", Pages = { page } };
        DiagramLayout.Apply(diagram);

        Assert.True(LegendLayout.Apply(diagram, "enterprise") >= 2);

        var report = LayoutAnalyzer.Analyze(diagram);
        Assert.DoesNotContain(report.Issues, i => i.Code == "text.overlaps-container" && i.Targets.Any(t => t.StartsWith("legend", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnEmptyDiagramProducesNoLegend()
    {
        var diagram = new Diagram { Id = "t", Pages = { new Page { Name = "p" } } };

        Assert.Empty(LegendBuilder.Entries(diagram, "enterprise"));
    }

    [Fact]
    public void AnUnknownPaletteFallsBackRatherThanThrowing()
    {
        var entries = LegendBuilder.Entries(TwoRoles(), "no-such-palette");

        Assert.NotEmpty(entries);
    }
}
