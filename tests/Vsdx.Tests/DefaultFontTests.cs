using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A delivered file must name a font.
///
/// When neither the object nor the diagram set one, the writer emitted no Font cell at all —
/// measured: a diagram without typography produced empty FaceNames and no Font cells. The package
/// then depends on whatever the reader machine defaults to, which is not reproducible and, for
/// Chinese labels, may not even contain the glyphs.
///
/// A default is a decision, so it is named in one place (FontDefaults) rather than inlined wherever
/// a font happens to be needed.
/// </summary>
public sealed class DefaultFontTests
{
    private static Diagram Bare()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 8, Height = 6 },
            Nodes = { new Node { Id = "n", Text = "N", Bounds = new Bounds(4, 3, 2, 1) } }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    private static (Diagram Written, string Path) Create(Diagram diagram)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-font-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = false, ValidateAfterSave = false });
        return (new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void ADiagramWithoutAnyFontStillGetsOne()
    {
        var (written, path) = Create(Bare());
        try
        {
            var node = written.Pages[0].Nodes[0];

            Assert.False(string.IsNullOrWhiteSpace(node.Style?.Font),
                "node came back with no font, so the file depends on the reader's default");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheDefaultIsTheSameEverywhere()
    {
        // One default, not one per code path — otherwise different object kinds drift apart.
        var (written, path) = Create(Bare());
        try
        {
            Assert.Equal(FontDefaults.Family, written.Pages[0].Nodes[0].Style?.Font);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnExplicitFontStillWins()
    {
        var diagram = Bare();
        diagram.Pages[0].Nodes[0].Style = new ShapeStyle { Font = "Courier New" };

        var (written, path) = Create(diagram);
        try
        {
            Assert.Equal("Courier New", written.Pages[0].Nodes[0].Style?.Font);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ADiagramLevelFontWinsOverTheDefault()
    {
        // Three levels: object, diagram, default. Diagram sits between the other two.
        var diagram = Bare();
        diagram.Style = new DiagramStyle { Font = "Verdana" };

        var (written, path) = Create(diagram);
        try
        {
            Assert.Equal("Verdana", written.Pages[0].Nodes[0].Style?.Font);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheDefaultIsACjkCapableFamily()
    {
        // A Latin-only default would render Chinese labels as boxes on a machine without fallback.
        // The name may not be installed here; what matters is that we ask for a family that covers
        // CJK where it exists.
        Assert.False(string.IsNullOrWhiteSpace(FontDefaults.Family));

        Assert.True(
            FontDefaults.Family.Contains("YaHei", StringComparison.OrdinalIgnoreCase) ||
            FontDefaults.Family.Contains("Hei", StringComparison.OrdinalIgnoreCase) ||
            FontDefaults.Family.Contains("Sans", StringComparison.OrdinalIgnoreCase),
            $"default '{FontDefaults.Family}' does not look CJK-capable");
    }
}
