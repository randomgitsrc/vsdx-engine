using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A delivered diagram should land on a paper size a reader can print.
///
/// `canvas.mode=auto` fits the page to the content's bounding box, which produced 8.55x9.68,
/// 11.00x11.85 and 15.57x10.45 for the three real-world scenarios — none of them A4, all of them a
/// custom size that a print dialog has to scale. The engine already reports when a fixed sheet is
/// too small (`canvas.exceeded`), so the missing piece is a mode that picks the sheet for you.
///
/// `paper` picks the smallest standard size that contains the content, and reports when it had to
/// scale the drawing down to fit.
/// </summary>
public sealed class PaperSizeTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// A diagram whose content is large enough to need more than A5.
    ///
    /// Anchors alone do not achieve that: the layered engine re-places every node, so anchors that
    /// look spread out in the IR end up on one row (measured: 4.15 x 1.0, which A5 holds). The node
    /// count and label lengths drive the width instead, so this fixture uses many nodes.
    /// </summary>
    private static string WideIr()
    {
        // Six nodes put the content around 11 x 1 inches: wider than A5 and A4 portrait, narrower
        // than A3 landscape. Fourteen nodes overshot every standard sheet (29.45"), which is a
        // different test — that one checks the `canvas.scaled` report.
        var nodes = string.Join(",",
            Enumerable.Range(0, 6).Select(i => $$"""{"id":"n{{i}}","label":"Node {{i}}"}"""));
        var edges = string.Join(",",
            Enumerable.Range(1, 5).Select(i => $$"""{"id":"e{{i}}","from":"n{{i - 1}}","to":"n{{i}}"}"""));

        return $$"""
        {"irVersion":"1.0","id":"p","kind":"architecture",
         "nodes":[{{nodes}}],"edges":[{{edges}}]}
        """;
    }

    private static (ExecutionResult Result, Diagram Diagram, string Path) Run(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(WideIr(), Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-paper-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, path);
        return (result, new VsdxEngine().Read(path).Diagram, path);
    }

    /// <summary>Standard sheet sizes in inches, portrait orientation.</summary>
    private static readonly (string Name, double W, double H)[] Standards =
    {
        ("A4", 8.27, 11.69),
        ("A3", 11.69, 16.54),
        ("A2", 16.54, 23.39),
        ("Letter", 8.5, 11.0),
        ("Tabloid", 11.0, 17.0)
    };

    [Fact]
    public void PaperModeProducesAStandardSize()
    {
        var (_, diagram, path) = Run("""{"specVersion":"1.0","canvas":{"mode":"paper"}}""");
        try
        {
            var size = diagram.Pages[0].Size!;

            bool matches = Standards.Any(s =>
                (Math.Abs(size.Width - s.W) < 0.05 && Math.Abs(size.Height - s.H) < 0.05) ||
                (Math.Abs(size.Width - s.H) < 0.05 && Math.Abs(size.Height - s.W) < 0.05));

            Assert.True(matches, $"{size.Width:F2}x{size.Height:F2} is not a standard sheet");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheSheetChosenIsTheSmallestOneThatHolds()
    {
        var (_, auto, autoPath) = Run("""{"specVersion":"1.0","canvas":{"mode":"auto"}}""");
        var (_, paper, paperPath) = Run("""{"specVersion":"1.0","canvas":{"mode":"paper"}}""");
        try
        {
            var content = auto.Pages[0].Size!;
            var sheet = paper.Pages[0].Size!;

            Assert.True(sheet.Width >= content.Width - 0.01 && sheet.Height >= content.Height - 0.01,
                $"sheet {sheet.Width:F2}x{sheet.Height:F2} does not hold content " +
                $"{content.Width:F2}x{content.Height:F2}");

            // And no standard sheet noticeably smaller would have done. The comparison is by area,
            // because A3 and Tabloid are close enough that an axis-wise test flags one as "smaller"
            // while it is really the other orientation of the same sheet.
            double sheetArea = sheet.Width * sheet.Height;

            var smaller = Standards
                .Where(s => s.W * s.H < sheetArea * 0.95)
                .Where(s => (s.W >= content.Width && s.H >= content.Height) ||
                            (s.H >= content.Width && s.W >= content.Height))
                .ToList();

            Assert.True(smaller.Count == 0,
                $"a smaller sheet would have fitted: {string.Join(", ", smaller.Select(s => s.Name))}");
        }
        finally { File.Delete(autoPath); File.Delete(paperPath); }
    }

    [Fact]
    public void ChoosingASheetIsReported()
    {
        var (result, _, path) = Run("""{"specVersion":"1.0","canvas":{"mode":"paper"}}""");
        try
        {
            Assert.Contains(result.AppliedDesignNotes,
                n => n.Contains("canvas.paper", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnExplicitFixedSizeIsHonouredWhenShrinkIsOff()
    {
        // `paper` is a request for a suggestion; `fixed` is an instruction. ShrinkToFit defaults to
        // true, so a fixed sheet that is larger than the content gets trimmed by design — turning it
        // off is what makes the instruction absolute.
        var (_, diagram, path) = Run(
            """{"specVersion":"1.0","canvas":{"mode":"fixed","width":30,"height":20,"shrinkToFit":false}}""");
        try
        {
            Assert.Equal(30, diagram.Pages[0].Size!.Width, 1);
            Assert.Equal(20, diagram.Pages[0].Size!.Height, 1);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AutoStillFitsContent()
    {
        // The existing behaviour must not change unless paper is asked for.
        var (_, diagram, path) = Run("""{"specVersion":"1.0","canvas":{"mode":"auto"}}""");
        try
        {
            var size = diagram.Pages[0].Size!;
            Assert.True(size.Width < 20 && size.Height < 20,
                $"auto produced {size.Width:F2}x{size.Height:F2}");
        }
        finally { File.Delete(path); }
    }
}
