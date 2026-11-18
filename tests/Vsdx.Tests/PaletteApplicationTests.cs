using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A named palette has to change how the diagram looks.
///
/// `theme.palette` is declared in the schema as an unconstrained string and carried on ThemeSpec,
/// but nothing read it: two different palettes produced byte-identical output, `applied` listed
/// nothing and `unmetConstraints` stayed empty. A caller could ask for a visual style, be told the
/// run succeeded, and receive exactly the same picture as before.
///
/// This is the core promise of the style system — same content, different intent, visible
/// difference — so silence here is worse than an error.
/// </summary>
public sealed class PaletteApplicationTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string RolesIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture","nodes":[
      {"id":"db","label":"DB","role":"database"},
      {"id":"svc","label":"Svc","role":"service"},
      {"id":"gw","label":"GW","role":"gateway"}]}
    """;

    private sealed record Outcome(ExecutionResult Result, Diagram Diagram, string Path);

    private static Outcome Run(string specJson, string irJson = RolesIr)
    {
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-palette-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, output);
        var diagram = new VsdxEngine().Read(output).Diagram;

        return new Outcome(result, diagram, output);
    }

    private static List<string?> Fills(Diagram diagram) =>
        diagram.Pages[0].Nodes.Select(n => n.Style?.Fill).ToList();

    [Fact]
    public void TwoDifferentPalettesProduceDifferentFills()
    {
        var a = Run("""{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");
        var b = Run("""{"specVersion":"1.0","theme":{"palette":"muted"}}""");
        try
        {
            Assert.NotEqual(Fills(a.Diagram), Fills(b.Diagram));
        }
        finally
        {
            File.Delete(a.Path);
            File.Delete(b.Path);
        }
    }

    [Fact]
    public void APaletteActuallyColoursTheNodes()
    {
        // Not merely "different from each other" — different from doing nothing at all.
        var none = Run("""{"specVersion":"1.0"}""");
        var enterprise = Run("""{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");
        try
        {
            Assert.NotEqual(Fills(none.Diagram), Fills(enterprise.Diagram));
        }
        finally
        {
            File.Delete(none.Path);
            File.Delete(enterprise.Path);
        }
    }

    [Fact]
    public void RolesWithinOnePaletteAreVisuallyDistinct()
    {
        var run = Run("""{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");
        try
        {
            var fills = Fills(run.Diagram).Where(f => f is not null).Distinct().ToList();

            // database, service and gateway must not all collapse to one colour, or the diagram
            // has lost the role distinction that the palette exists to express.
            Assert.True(fills.Count >= 2, $"expected distinct role colours, got {string.Join(",", fills)}");
        }
        finally
        {
            File.Delete(run.Path);
        }
    }

    [Fact]
    public void AnUnknownPaletteIsReported()
    {
        var run = Run("""{"specVersion":"1.0","theme":{"palette":"does-not-exist"}}""");
        try
        {
            Assert.Contains(run.Result.UnmetConstraints,
                u => u.Code.Contains("palette", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(run.Path);
        }
    }

    [Fact]
    public void ARecognisedPaletteIsNotReportedAsUnmet()
    {
        var run = Run("""{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");
        try
        {
            Assert.DoesNotContain(run.Result.UnmetConstraints,
                u => u.Code.Contains("palette", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(run.Result.AppliedDesignNotes, n => n.Contains("palette", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(run.Path);
        }
    }

    [Fact]
    public void ExplicitObjectStyleStillWinsOverThePalette()
    {
        // A palette is a default; a colour the author set on the object is a decision.
        //
        // Checked at the model level rather than through IR, because IrNode deliberately carries
        // no Style property — node styling is a model concern, and pretending otherwise in a
        // fixture would test a path that does not exist.
        var diagram = new Diagram
        {
            Id = "p",
            Pages =
            {
                new Page
                {
                    Name = "page-1",
                    Size = new PageSize { Width = 11, Height = 8.5 },
                    Nodes =
                    {
                        new Node
                        {
                            Id = "db", Text = "DB", Type = "database",
                            Bounds = new Bounds(3, 5, 2, 1),
                            Style = new ShapeStyle { Fill = "#123456" }
                        },
                        new Node { Id = "svc", Text = "Svc", Type = "service", Bounds = new Bounds(7, 5, 2, 1) }
                    }
                }
            }
        };

        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;

        DesignSpecApplier.Apply(diagram, spec);

        var db = diagram.Pages[0].Nodes.Single(n => n.Id == "db");
        var svc = diagram.Pages[0].Nodes.Single(n => n.Id == "svc");

        Assert.Equal("#123456", db.Style?.Fill);
        Assert.NotEqual("#FFFFFF", svc.Style?.Fill);
    }

    [Fact]
    public void RoleColorsOverrideThePalette()
    {
        // Layer 2 beats layer 1: a role-specific colour is more specific than the palette default.
        var run = Run(
            """{"specVersion":"1.0","theme":{"palette":"enterprise","roleColors":{"service":"#AA0000"}}}""");
        try
        {
            var svc = run.Diagram.Pages[0].Nodes.Single(n => n.Id == "svc");
            Assert.Equal("#AA0000", svc.Style?.Fill);
        }
        finally
        {
            File.Delete(run.Path);
        }
    }
}
