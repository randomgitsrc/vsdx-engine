using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Typography must reach every kind of text, not just node labels.
///
/// Applying a font family and base size touched nodes only. Groups kept a hardcoded 12pt with no
/// family, edge labels had no size at all, and text blocks fell back to their role default. So a
/// spec asking for a 14pt Georgia diagram produced one that was 14pt Georgia in the boxes and
/// something else everywhere else — the opposite of a unified visual system, and the reason
/// "same content, different intent" only half worked.
/// </summary>
public sealed class TypographyCoverageTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string MixedIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "containers":[{"id":"g1","label":"Layer"}],
     "nodes":[{"id":"a","label":"A","containerId":"g1"},{"id":"b","label":"B","containerId":"g1"}],
     "edges":[{"id":"e","from":"a","to":"b","label":"calls"}],
     "textBlocks":[{"id":"t","text":"Title","role":"title",
                    "box":{"x":3,"y":5,"width":3,"height":0.4}}]}
    """;

    private static (Diagram Diagram, string Path) Run(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(MixedIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-typo-{Guid.NewGuid():N}.vsdx");
        new ProtocolRunner().Execute(ir, spec, path);

        return (new VsdxEngine().Read(path).Diagram, path);
    }

    private const string Spec =
        """{"specVersion":"1.0","typography":{"fontFamily":"Georgia","baseSize":14}}""";

    [Fact]
    public void NodesGetTheSpecFont()
    {
        var (diagram, path) = Run(Spec);
        try
        {
            Assert.All(diagram.Pages[0].Nodes, n => Assert.Equal("Georgia", n.Style?.Font));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GroupsGetTheSpecFont()
    {
        var (diagram, path) = Run(Spec);
        try
        {
            Assert.All(diagram.Pages[0].Groups, g => Assert.Equal("Georgia", g.Style?.Font));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EdgeLabelsGetTheSpecFont()
    {
        var (diagram, path) = Run(Spec);
        try
        {
            Assert.All(diagram.Pages[0].Edges, e => Assert.Equal("Georgia", e.LabelFont));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TextBlocksGetTheSpecFont()
    {
        var (diagram, path) = Run(Spec);
        try
        {
            Assert.All(diagram.Pages[0].TextBlocks, t => Assert.Equal("Georgia", t.Style?.Font));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TextBlockSizesScaleWithBaseSize()
    {
        // A title should stay larger than body text, but both should move with the base size.
        var (small, p1) = Run("""{"specVersion":"1.0","typography":{"fontFamily":"Georgia","baseSize":9}}""");
        var (large, p2) = Run("""{"specVersion":"1.0","typography":{"fontFamily":"Georgia","baseSize":24}}""");
        try
        {
            double s1 = small.Pages[0].TextBlocks[0].Style?.FontSize ?? 0;
            double s2 = large.Pages[0].TextBlocks[0].Style?.FontSize ?? 0;

            Assert.True(s2 > s1 + 1, $"text block size did not scale: {s1:F1} -> {s2:F1}");
        }
        finally { File.Delete(p1); File.Delete(p2); }
    }

    [Fact]
    public void EdgeLabelSizesScaleWithBaseSize()
    {
        var (small, p1) = Run("""{"specVersion":"1.0","typography":{"fontFamily":"Georgia","baseSize":9}}""");
        var (large, p2) = Run("""{"specVersion":"1.0","typography":{"fontFamily":"Georgia","baseSize":24}}""");
        try
        {
            double s1 = small.Pages[0].Edges[0].LabelFontSize ?? 0;
            double s2 = large.Pages[0].Edges[0].LabelFontSize ?? 0;

            Assert.True(s2 > s1 + 1, $"edge label size did not scale: {s1:F1} -> {s2:F1}");
        }
        finally { File.Delete(p1); File.Delete(p2); }
    }

    [Fact]
    public void ExplicitObjectFontStillWins()
    {
        // A palette of defaults must not override a decision the author made.
        var diagram = new Diagram
        {
            Id = "p",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 10, Height = 6 },
                    Nodes =
                    {
                        new Node
                        {
                            Id = "a", Text = "A", Bounds = new Bounds(3, 3, 2, 1),
                            Style = new ShapeStyle { Font = "Courier New", FontSize = 20 }
                        }
                    }
                }
            }
        };

        var spec = JsonSerializer.Deserialize<DesignSpec>(Spec, Json)!;
        DesignSpecApplier.Apply(diagram, spec);

        var node = diagram.Pages[0].Nodes[0];
        Assert.Equal("Courier New", node.Style?.Font);
        Assert.Equal(20, node.Style?.FontSize);
    }
}
