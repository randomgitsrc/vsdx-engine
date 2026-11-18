using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Container bands must take their colour from the palette, not from a fixed array.
///
/// The band fills were three hardcoded arrays of five colours each, chosen before palettes existed
/// and never revisited. Two different palettes produced byte-identical container colours, so the
/// containers were the one part of the diagram the palette never reached — visible as a diagram
/// whose boxes changed palette and whose bands did not.
///
/// Sibling bands also have to stay distinguishable. The arrays rotated through five tints on
/// purpose; collapsing to a single colour would reintroduce the "one undifferentiated block"
/// problem the arrays were added to solve.
/// </summary>
public sealed class ContainerPaletteTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string ThreeBandsIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "containers":[{"id":"g1","label":"L1"},{"id":"g2","label":"L2"},{"id":"g3","label":"L3"}],
     "nodes":[{"id":"a","label":"A","containerId":"g1"},
              {"id":"b","label":"B","containerId":"g2"},
              {"id":"c","label":"C","containerId":"g3"}]}
    """;

    private static (Diagram Diagram, string Path) Run(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(ThreeBandsIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-band-{Guid.NewGuid():N}.vsdx");
        new ProtocolRunner().Execute(ir, spec, path);

        return (new VsdxEngine().Read(path).Diagram, path);
    }

    private static List<string?> BandFills(Diagram d) =>
        d.Pages[0].Groups.Select(g => g.Style?.Fill).ToList();

    [Fact]
    public void TwoPalettesProduceDifferentBandFills()
    {
        var (a, p1) = Run("""{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");
        var (b, p2) = Run("""{"specVersion":"1.0","theme":{"palette":"muted"}}""");
        try
        {
            Assert.NotEqual(BandFills(a), BandFills(b));
        }
        finally { File.Delete(p1); File.Delete(p2); }
    }

    [Fact]
    public void ASinglePaletteStillVariesBetweenBands()
    {
        // The hardcoded arrays rotated through several tints for a reason: identical bands read as
        // one block. Replacing them must keep that property.
        var (diagram, path) = Run("""{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");
        try
        {
            var fills = BandFills(diagram).Where(f => f is not null).Distinct().ToList();

            Assert.True(fills.Count >= 3,
                $"expected distinct band colours, got {fills.Count}: {string.Join(",", fills)}");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BandTextStaysReadableAgainstItsFill()
    {
        // Gives text colour from the same palette entry, so contrast cannot drift apart from fill.
        var (diagram, path) = Run("""{"specVersion":"1.0","theme":{"palette":"midnight"}}""");
        try
        {
            foreach (var group in diagram.Pages[0].Groups)
            {
                Assert.False(string.IsNullOrWhiteSpace(group.Style?.TextColor),
                    $"{group.Id} has no text colour");
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExplicitContainerColourStillWins()
    {
        // A palette is a default. A colour set on the container is a decision.
        var diagram = new Diagram
        {
            Id = "p",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 10, Height = 6 },
                    Groups =
                    {
                        new Group
                        {
                            Id = "g1", Members = { "a" }, Bounds = new Bounds(5, 3, 4, 2),
                            Style = new ShapeStyle { Fill = "#123456" }
                        }
                    },
                    Nodes = { new Node { Id = "a", Text = "A", Bounds = new Bounds(5, 3, 2, 1) } }
                }
            }
        };

        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;
        DesignSpecApplier.Apply(diagram, spec);

        Assert.Equal("#123456", diagram.Pages[0].Groups[0].Style?.Fill);
    }

    [Fact]
    public void WithoutAPaletteBandsStillGetSomeColour()
    {
        // The arrays existed so containers were not left on the writer's default. Removing them
        // without a palette fallback would regress that.
        var (diagram, path) = Run("""{"specVersion":"1.0"}""");
        try
        {
            Assert.All(diagram.Pages[0].Groups, g =>
                Assert.False(string.IsNullOrWhiteSpace(g.Style?.Fill), $"{g.Id} lost its fill"));
        }
        finally { File.Delete(path); }
    }
}
