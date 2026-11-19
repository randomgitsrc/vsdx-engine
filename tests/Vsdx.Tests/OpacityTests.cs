using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `ShapeStyle.Opacity` is declared on the model, permitted by the schema, and listed by the writer among the
/// fields it reports as unsupported — so setting it changes nothing and the caller is told, correctly, that it was
/// dropped.
///
/// OfficeIMO has no opacity API at all: `VisioShape` exposes `FillColor`, `FillPattern`, `LineColor`, `LinePattern`
/// and a handful of `Set*` methods, and nothing for transparency. But the page part is XML this project already
/// writes directly for free paths (`VisioFreePathBridge`), and Visio's own cells are `FillForegndTrans` and
/// `LineColorTrans`.
///
/// Note the inversion: Visio's `*Trans` is 0 for opaque and 1 for invisible, while `Opacity` is the other way round.
/// </summary>
public sealed class OpacityTests
{
    private static Diagram FadedNode()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 10, Height = 8 } };
        page.Nodes.Add(new Node
        {
            Id = "a", Text = "A",
            Bounds = new Bounds(5, 4, 1.4, 0.6),
            Style = new ShapeStyle { Fill = "#70AD47", Stroke = "#507E32", Opacity = 0.45 }
        });

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void OpacityReachesThePackage()
    {
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-op-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(FadedNode(), output, new WriteOptions { AutoLayout = true });

            using var zip = System.IO.Compression.ZipFile.OpenRead(output);
            using var reader = new StreamReader(zip.Entries
                .First(e => e.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)
                         && e.FullName != "visio/pages/pages.xml").Open());
            var xml = reader.ReadToEnd();

            Assert.Contains("FillForegndTrans", xml, StringComparison.Ordinal);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public void TheValueIsInvertedForVisio()
    {
        // Opacity 0.45 means "45% visible", so Visio's transparency must be 0.55.
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-op2-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(FadedNode(), output, new WriteOptions { AutoLayout = true });

            using var zip = System.IO.Compression.ZipFile.OpenRead(output);
            using var reader = new StreamReader(zip.Entries
                .First(e => e.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)
                         && e.FullName != "visio/pages/pages.xml").Open());
            var xml = reader.ReadToEnd();

            // 1 - 0.45 = 0.55, written with Visio's own precision
            Assert.Matches(@"FillForegndTrans"" V=""0\.55", xml);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public void NOpacityMeansNoTransparencyCells()
    {
        // A shape with no opacity must not gain a transparency cell: it would be reported as a change it did not ask
        // for, and 0 and "unset" render identically anyway.
        var page = new Page { Name = "p", Size = new PageSize { Width = 10, Height = 8 } };
        page.Nodes.Add(new Node
        {
            Id = "a", Text = "A", Bounds = new Bounds(5, 4, 1.4, 0.6),
            Style = new ShapeStyle { Fill = "#70AD47" }
        });

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-op3-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(new Diagram { Id = "t", Pages = { page } }, output,
                new WriteOptions { AutoLayout = true });

            using var zip = System.IO.Compression.ZipFile.OpenRead(output);
            using var reader = new StreamReader(zip.Entries
                .First(e => e.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)
                         && e.FullName != "visio/pages/pages.xml").Open());

            Assert.DoesNotContain("FillForegndTrans", reader.ReadToEnd(), StringComparison.Ordinal);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }
}
