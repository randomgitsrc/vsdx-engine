using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Renderer;
using Xunit;

namespace Vsdx.Tests;

/// <summary>RenderTests: SVG and PNG output produced from the real package.</summary>
public sealed class RenderTests
{
    private static (string dir, string vsdx) Build(string label)
    {
        var dir = TestData.NewTempDir(label);
        var vsdx = Path.Combine(dir, "d.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), vsdx);
        return (dir, vsdx);
    }

    [Fact]
    public void Render_ProducesSvg()
    {
        var (dir, vsdx) = Build("render-svg");
        var svg = Path.Combine(dir, "out.svg");

        var engine = new VsdxEngine();
        var result = engine.Render(new RenderRequest
        {
            InputPath = vsdx, OutputPath = svg, Format = "svg"
        });

        Assert.Single(result.Pages);
        Assert.True(File.Exists(svg));
        var text = File.ReadAllText(svg);
        Assert.Contains("<svg", text);
        Assert.Contains("</svg>", text);
        Assert.Contains("Alpha", text);
    }

    [Fact]
    public void Render_ProducesPng()
    {
        var (dir, vsdx) = Build("render-png");
        var png = Path.Combine(dir, "out.png");

        var engine = new VsdxEngine();
        var result = engine.Render(new RenderRequest
        {
            InputPath = vsdx, OutputPath = png, Format = "png", Dpi = 96
        });

        Assert.Single(result.Pages);
        Assert.True(File.Exists(png));

        var header = new byte[8];
        using (var stream = File.OpenRead(png)) stream.ReadExactly(header);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, header);
        Assert.True(result.Pages[0].Width > 0);
        Assert.True(result.Pages[0].Height > 0);
    }

    [Fact]
    public void Render_ProducesCjkGlyphs()
    {
        var (dir, vsdx) = Build("render-cjk");
        var svg = Path.Combine(dir, "out.svg");

        var engine = new VsdxEngine();
        engine.Render(new RenderRequest { InputPath = vsdx, OutputPath = svg, Format = "svg" });

        var text = File.ReadAllText(svg);
        Assert.Contains("中文节点", text);
    }

    [Fact]
    public void Render_ResolvesCjkFontForPng()
    {
        var (dir, vsdx) = Build("render-cjk-png");
        var png = Path.Combine(dir, "out.png");

        var engine = new VsdxEngine();
        var result = engine.Render(new RenderRequest
        {
            InputPath = vsdx, OutputPath = png, Format = "png"
        });

        // A CJK-capable font must be selected, otherwise Chinese renders as boxes.
        Assert.NotNull(result.FontUsed);
        Assert.True(File.Exists(result.FontUsed));
    }

    [Fact]
    public void Render_SupportsDpiScaling()
    {
        var (dir, vsdx) = Build("render-dpi");

        var engine = new VsdxEngine();
        var low = engine.Render(new RenderRequest
        {
            InputPath = vsdx, OutputPath = Path.Combine(dir, "low.png"), Format = "png", Dpi = 72
        });
        var high = engine.Render(new RenderRequest
        {
            InputPath = vsdx, OutputPath = Path.Combine(dir, "high.png"), Format = "png", Dpi = 192
        });

        Assert.True(high.Pages[0].Width > low.Pages[0].Width);
    }

    [Fact]
    public void Render_SupportsTransparentBackground()
    {
        var (dir, vsdx) = Build("render-transparent");
        var png = Path.Combine(dir, "t.png");

        var engine = new VsdxEngine();
        var result = engine.Render(new RenderRequest
        {
            InputPath = vsdx, OutputPath = png, Format = "png", Transparent = true
        });

        Assert.Single(result.Pages);
        Assert.True(File.Exists(png));
    }

    [Fact]
    public void Render_SupportsCustomBackground()
    {
        var (dir, vsdx) = Build("render-bg");
        var png = Path.Combine(dir, "b.png");

        var engine = new VsdxEngine();
        var result = engine.Render(new RenderRequest
        {
            InputPath = vsdx, OutputPath = png, Format = "png", Background = "#F5F5F5"
        });

        Assert.Single(result.Pages);
        Assert.Empty(result.Compatibility.Errors);
    }

    [Fact]
    public void Render_ReportsMissingPage()
    {
        var (dir, vsdx) = Build("render-missing-page");

        var engine = new VsdxEngine();
        var result = engine.Render(new RenderRequest
        {
            InputPath = vsdx,
            OutputPath = Path.Combine(dir, "x.png"),
            Format = "png",
            PageName = "No Such Page"
        });

        Assert.Empty(result.Pages);
        Assert.Contains(result.Compatibility.Errors, e => e.Code == "render.page-not-found");
    }

    [Fact]
    public void Render_OverwritesExistingOutput()
    {
        var (dir, vsdx) = Build("render-overwrite");
        var png = Path.Combine(dir, "o.png");
        File.WriteAllText(png, "stale");

        var engine = new VsdxEngine();
        var result = engine.Render(new RenderRequest
        {
            InputPath = vsdx, OutputPath = png, Format = "png"
        });

        Assert.Single(result.Pages);
        Assert.True(new FileInfo(png).Length > 100);
    }

    [Fact]
    public void FontResolver_DetectsCjkRequirement()
    {
        Assert.True(FontResolver.RequiresCjk(new[] { "中文" }));
        Assert.True(FontResolver.RequiresCjk(new[] { "English", "混排 text" }));
        Assert.False(FontResolver.RequiresCjk(new[] { "Plain ASCII only" }));
        Assert.False(FontResolver.RequiresCjk(new string?[] { null, "" }));
    }
}
