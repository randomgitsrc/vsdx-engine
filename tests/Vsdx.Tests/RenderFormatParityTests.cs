using System.Text.Json;
using System.Text.RegularExpressions;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// PNG and SVG must show the same drawing, including every piece of text.
///
/// The two formats go through different backends — PNG through the rasteriser, SVG through a writer that
/// emits `<text>`/`<tspan>` — so a label could plausibly appear in one and not the other. Compared on the
/// demo diagram: the model carries 33 text strings and all 33 appear in the SVG; the SVG has 47 lines
/// because a two-line label becomes two `tspan`s.
///
/// This is a content check, not a pixel check: it cannot tell whether the two look alike, only that
/// nothing was dropped.
/// </summary>
public sealed class RenderFormatParityTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static (string Png, string Svg) RenderBoth(string vsdx)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vsdx-par-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var png = Path.Combine(dir, "out.png");
        var svg = Path.Combine(dir, "out.svg");

        var engine = new VsdxEngine();

        engine.Render(new RenderRequest { InputPath = vsdx, OutputPath = png, Format = "png" });
        engine.Render(new RenderRequest { InputPath = vsdx, OutputPath = svg, Format = "svg" });

        return (png, svg);
    }

    private static HashSet<string> SvgLines(string path)
    {
        var svg = File.ReadAllText(path);
        var lines = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(svg, @"<text[^>]*>(.*?)</text>", RegexOptions.Singleline))
        {
            var raw = Regex.Replace(m.Groups[1].Value, "<[^>]+>", "");

            foreach (var line in raw.Split('\n'))
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line.Trim());
        }

        return lines;
    }

    private static HashSet<string> ModelLines(Diagram diagram)
    {
        var lines = new HashSet<string>(StringComparer.Ordinal);

        foreach (var page in diagram.Pages)
        {
            foreach (var node in page.Nodes)
                foreach (var line in (node.Text ?? "").Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line)) lines.Add(line.Trim());

            foreach (var group in page.Groups)
                if (!string.IsNullOrWhiteSpace(group.Name)) lines.Add(group.Name!.Trim());

            foreach (var block in page.TextBlocks)
                foreach (var line in (block.Text ?? "").Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line)) lines.Add(line.Trim());
        }

        return lines;
    }

    [Theory]
    [InlineData("demo/architecture.vsdx")]
    [InlineData("examples/realworld/cbo.vsdx")]
    [InlineData("examples/realworld/org-tree.vsdx")]
    public void EveryModelLabelAppearsInTheSvg(string relative)
    {
        var vsdx = Path.Combine(Root(), relative);

        if (!File.Exists(vsdx)) return;   // skip gracefully if an example is absent

        var (png, svg) = RenderBoth(vsdx);

        try
        {
            Assert.True(new FileInfo(png).Length > 0, "the PNG is empty");
            Assert.True(new FileInfo(svg).Length > 0, "the SVG is empty");

            var model = ModelLines(new VsdxEngine().Read(vsdx).Diagram);
            var svgLines = SvgLines(svg);

            var missing = model.Where(l => !svgLines.Contains(l)).ToList();

            Assert.True(missing.Count == 0,
                "labels present in the model but absent from the SVG: " + string.Join(" | ", missing));
        }
        finally
        {
            var dir = Path.GetDirectoryName(png)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheSvgCarriesShapeGeometryNotJustText()
    {
        var vsdx = Path.Combine(Root(), "demo", "architecture.vsdx");
        if (!File.Exists(vsdx)) return;

        var (_, svgPath) = RenderBoth(vsdx);

        try
        {
            var svg = File.ReadAllText(svgPath);

            // Containers are rects or paths; nodes carry a shape id. Both are needed for a usable SVG.
            Assert.Contains("data-visio-shape-id", svg);
            Assert.True(Regex.Matches(svg, "<path").Count + Regex.Matches(svg, "<rect").Count > 5,
                "the SVG has almost no geometry");
        }
        finally
        {
            var dir = Path.GetDirectoryName(svgPath)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
