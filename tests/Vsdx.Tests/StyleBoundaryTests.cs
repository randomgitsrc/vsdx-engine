using Vsdx.Adapters;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Records which style fields cannot reach a file, and why the list is not a to-do.
///
/// Four remain: `shadow`, `radius`, `lineJoin`, `paddingBottom`. I checked each against OfficeIMO 3.6.1 and against
/// what the IR can express:
///
///   shadow         no `Shadow` property on `VisioShape`; a real one needs `ShapeShdwShow`, `ShdwForegnd` and an
///                  offset, so it is a group of cells rather than one
///   radius         needs the Geometry rows to be rewritten, not a cell
///   lineJoin       no property, but `LineCap` is writable — possible, though invisible at this output size
///   paddingBottom  no property; `BottomMargin` is writable
///
/// **The reason not to chase them is upstream of the writer.** `IrNode` carries id, label, role, container, status
/// and an optional anchor — no fill, no stroke, no bold. Style comes from `theme.palette` by role. That is
/// deliberate: `Anchor` is documented as "IR must not degenerate into a coordinate list", and per-node styling
/// would be the same failure with colours. So these four are unreachable from the IR by design, and the writer's
/// report about them is accurate rather than a gap.
///
/// This test exists so the list is not quietly extended to fields that *are* reachable — the distinction that
/// `opacity` crossed.
/// </summary>
public sealed class StyleBoundaryTests
{
    [Fact]
    public void TheUnsupportedStyleFieldsAreExactlyTheOnesWithNoWriterPath()
    {
        var style = new ShapeStyle
        {
            Shadow = true,
            Radius = 0.1,
            LineJoin = "round",
            PaddingBottom = 0.2,
            Opacity = 0.5   // supported now; must not reappear in the report
        };

        var page = new Page { Name = "p", Size = new PageSize { Width = 10, Height = 8 } };
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(5, 4, 1.4, 0.6), Style = style });

        // Through the public write path, which is what a caller actually sees.
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-style-{Guid.NewGuid():N}.vsdx");
        string dropped;

        try
        {
            var result = new VsdxEngine().Create(
                new Diagram { Id = "t", Pages = { page } }, output, new WriteOptions { AutoLayout = true });

            dropped = string.Join(",", result.Compatibility.Issues.Select(i => i.Code + " " + i.Message)).ToLowerInvariant();
        }
        finally { if (File.Exists(output)) File.Delete(output); }

        Assert.Contains("shadow", dropped);
        Assert.Contains("radius", dropped);
        Assert.Contains("linejoin", dropped);
        Assert.Contains("paddingbottom", dropped);

        Assert.DoesNotContain("opacity", dropped);
    }

    [Fact]
    public void TheIrOffersNoPerNodeStyle()
    {
        // A guard on the protocol, not the writer: if `IrNode` ever gains `Fill`, this boundary moves and the four
        // fields above stop being unreachable.
        var properties = typeof(Vsdx.Protocol.IrNode).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("Fill", properties);
        Assert.DoesNotContain("Stroke", properties);
        Assert.DoesNotContain("Shadow", properties);
        Assert.DoesNotContain("Radius", properties);
    }
}
