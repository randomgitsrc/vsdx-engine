using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Nodes declared in a band should be laid out in the order they were declared.
///
/// The converter gave every node the same placeholder X, so the engine's `OrderBy(Bounds.X)` had
/// nothing to sort on and fell through to `ThenBy(Id)` — alphabetical order. Measured on the
/// cross-border diagram: three data-layer nodes declared pg, redis, es came out es, pg, redis,
/// which is exactly alphabetical and unrelated to the author's intent.
///
/// Alphabetical order is not a design decision. IR sequence is: an author writing pg before redis
/// before es means something, and replacing it with id order produces crossings that were never
/// intended.
/// </summary>
public sealed class DeclarationOrderTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Deliberately declared in an order that contradicts alphabetical sorting: z, m, a.
    /// </summary>
    private const string BandIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "containers":[{"id":"band","label":"Band"}],
     "nodes":[{"id":"z","label":"Z","containerId":"band"},
              {"id":"m","label":"M","containerId":"band"},
              {"id":"a","label":"A","containerId":"band"}],
     "edges":[{"id":"e1","from":"z","to":"m"}]}
    """;

    private static Diagram Converted()
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(BandIr, Json)!;
        return DiagramIrConverter.ToDiagram(ir);
    }

    [Fact]
    public void TheConverterGivesNodesDistinctHorizontalPositions()
    {
        // Without this the engine has nothing to order by and silently sorts by id.
        var diagram = Converted();
        var xs = diagram.Pages[0].Nodes.Select(n => n.Bounds.X).ToList();

        Assert.Equal(xs.Count, xs.Distinct().Count());
    }

    [Fact]
    public void DeclaredOrderIsPreservedInTheInitialPositions()
    {
        var diagram = Converted();
        var byId = diagram.Pages[0].Nodes.ToDictionary(n => n.Id, n => n.Bounds.X, StringComparer.Ordinal);

        Assert.True(byId["z"] < byId["m"], $"z ({byId["z"]:F3}) should start left of m ({byId["m"]:F3})");
        Assert.True(byId["m"] < byId["a"], $"m ({byId["m"]:F3}) should start left of a ({byId["a"]:F3})");
    }

    [Fact]
    public void TheLayoutKeepsDeclaredOrderWithinABand()
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(BandIr, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);

        // The engine's own direction, stated rather than defaulted: the default transposes, and
        // in-band order then runs along Y instead of X.
        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Layered);

        var byId = diagram.Pages[0].Nodes.ToDictionary(n => n.Id, n => n.Bounds.X, StringComparer.Ordinal);

        Assert.True(byId["z"] < byId["a"],
            $"layout reversed the declaration: z at {byId["z"]:F2}, a at {byId["a"]:F2}");
    }

    [Fact]
    public void AnExplicitAnchorStillWins()
    {
        // The author's coordinates outrank the declaration index.
        const string anchored = """
        {"irVersion":"1.0","id":"p","kind":"architecture",
         "nodes":[{"id":"z","label":"Z","anchor":{"x":9,"y":4}},
                  {"id":"a","label":"A","anchor":{"x":2,"y":4}}],
         "edges":[{"id":"e1","from":"z","to":"a"}]}
        """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(anchored, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);
        var byId = diagram.Pages[0].Nodes.ToDictionary(n => n.Id, n => n.Bounds.X, StringComparer.Ordinal);

        Assert.Equal(2, byId["a"], 2);
        Assert.Equal(9, byId["z"], 2);
    }
}
