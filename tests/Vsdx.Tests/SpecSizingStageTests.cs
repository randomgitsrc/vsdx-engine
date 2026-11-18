using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A design spec must still size boxes to their text.
///
/// The spec path ran layout and page fitting but never called DiagramSizer.Apply — the stage that
/// grows a node's box to fit its label. Without it every node kept the converter's hardcoded
/// 2.00x0.90, whatever font size the spec asked for: baseSize 11 and baseSize 24 produced
/// identical boxes. Passing a spec was therefore worse than passing none, because the branch
/// without a spec does size.
///
/// Font size also has to be final before measuring, or the boxes are sized to text that is then
/// re-sized underneath them.
/// </summary>
public sealed class SpecSizingStageTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string LongLabelIr = """
    {"irVersion":"1.0","id":"a","kind":"architecture",
     "nodes":[{"id":"x","label":"A very long node label here"}]}
    """;

    private static (Diagram Diagram, ExecutionResult Result, string Path) Run(
        string? specJson, string irJson = LongLabelIr)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = specJson is null ? null : JsonSerializer.Deserialize<DesignSpec>(specJson, Json);

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-size-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, path);

        return (new VsdxEngine().Read(path).Diagram, result, path);
    }

    private static (double W, double H) Box(Diagram d)
    {
        var b = d.Pages[0].Nodes[0].Bounds;
        return (b.W, b.H);
    }

    [Fact]
    public void BoxesAreSizedToTextOnTheSpecPath()
    {
        // The converter's placeholder is a fixed 2.00 x 0.90. Sizing replaces it with something
        // derived from the text, so the assertion is that the box is *not* the placeholder — not
        // that it is wider. At 11pt this particular label needs less than 2.00.
        var (diagram, _, path) = Run("""{"specVersion":"1.0","typography":{"baseSize":11}}""");
        try
        {
            var (w, h) = Box(diagram);

            bool stillPlaceholder = Math.Abs(w - 2.00) < 0.01 && Math.Abs(h - 0.90) < 0.01;
            Assert.False(stillPlaceholder, $"box {w:F2}x{h:F2} is still the converter placeholder");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ALargerFontProducesALargerBox()
    {
        // The defect in one assertion: these were identical before the fix.
        var (small, _, p1) = Run("""{"specVersion":"1.0","typography":{"baseSize":11}}""");
        var (large, _, p2) = Run("""{"specVersion":"1.0","typography":{"baseSize":24}}""");
        try
        {
            var (wSmall, hSmall) = Box(small);
            var (wLarge, hLarge) = Box(large);

            Assert.True(wLarge > wSmall + 0.05 || hLarge > hSmall + 0.05,
                $"24pt box ({wLarge:F2}x{hLarge:F2}) is not larger than 11pt ({wSmall:F2}x{hSmall:F2})");
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
        }
    }

    [Fact]
    public void SpecPathAndDefaultPathAgreeAtTheSameFontSize()
    {
        // With no spec the engine sizes to text; with a spec it must not be worse.
        var (withSpec, _, p1) = Run(
            """{"specVersion":"1.0","typography":{"baseSize":11}}""");
        var (noSpec, _, p2) = Run(null);
        try
        {
            var (w1, h1) = Box(withSpec);
            var (w2, h2) = Box(noSpec);

            Assert.True(Math.Abs(w1 - w2) < 0.05,
                $"spec path {w1:F2} vs default path {w2:F2} disagree at the same font size");
            Assert.True(Math.Abs(h1 - h2) < 0.05,
                $"spec path {h1:F2} vs default path {h2:F2} disagree at the same font size");
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
        }
    }

    [Fact]
    public void SizedBoxesDoNotOverflowTheirText()
    {
        // Growing the font must not leave the label clipped, which is what sizing protects against.
        var (_, result, path) = Run("""{"specVersion":"1.0","typography":{"baseSize":24}}""");
        try
        {
            var diagram = new VsdxEngine().Read(path).Diagram;
            var report = LayoutAnalyzer.Analyze(diagram);

            Assert.DoesNotContain(report.Issues,
                i => i.Code.StartsWith("text.overflow", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SizingRunsAfterFontSizeIsFinal()
    {
        // Guard against a fix that sizes from the default font and then applies the spec's size.
        // If measuring happened first, a 24pt request would still produce an 11pt-sized box.
        var (small, _, p1) = Run("""{"specVersion":"1.0","typography":{"baseSize":8}}""");
        var (large, _, p2) = Run("""{"specVersion":"1.0","typography":{"baseSize":20}}""");
        try
        {
            var (w8, h8) = Box(small);
            var (w20, h20) = Box(large);

            double area8 = w8 * h8;
            double area20 = w20 * h20;

            Assert.True(area20 > area8 * 1.5,
                $"20pt area {area20:F2} is not meaningfully larger than 8pt area {area8:F2}");
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
        }
    }
}
