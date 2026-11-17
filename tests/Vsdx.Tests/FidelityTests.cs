using System.IO.Compression;
using System.Xml.Linq;
using Vsdx.Adapters;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Fidelity tests: the P0 defect from the Phase 8 audit was that saving an external VSDX added a
/// synthesised rectangle Geometry section to every shape that had none, overwriting circle,
/// diamond, custom-path and master-backed outlines.
///
/// Root cause (isolated in docs/phase8/p0-geometry-injection.md): OfficeIMO.Visio synthesises a
/// rectangle when a shape has neither geometry nor a master. Master-backed shapes round-trip
/// intact, so the fix is narrow — remember which shapes had no geometry and strip only the
/// synthesised section afterwards.
/// </summary>
public sealed class FidelityTests
{
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";

    // ------------------------------------------------------------------
    // Minimal external package builder: shapes with no Geometry section.
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds a valid VSDX whose single shape carries no Geometry section — the exact shape of
    /// file the audit found broken. Deliberately hand-written rather than produced by our writer,
    /// so the test does not inherit our writer's assumptions.
    /// </summary>
    internal static string WritePackageWithoutGeometry(
        string directory, string name, string shapeNameU = "Rectangle", string text = "Orders")
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);

        string page = $"""
<?xml version="1.0" encoding="utf-8"?>
<PageContents xmlns="http://schemas.microsoft.com/office/visio/2012/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<Shapes>
<Shape ID="1" NameU="{shapeNameU}" Type="Shape" LineStyle="0" FillStyle="0" TextStyle="0">
<Cell N="PinX" V="5"/><Cell N="PinY" V="5"/>
<Cell N="Width" V="2"/><Cell N="Height" V="1"/>
<Cell N="LocPinX" V="1"/><Cell N="LocPinY" V="0.5"/>
<Cell N="FillForegnd" V="#2E75B6"/><Cell N="LineColor" V="#1F4E79"/>
<Text>{text}</Text>
</Shape>
</Shapes>
</PageContents>
""";

        string pages = """
<?xml version="1.0" encoding="utf-8"?>
<Pages xmlns="http://schemas.microsoft.com/office/visio/2012/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<Page ID="0" NameU="Page-1" Name="Page-1"><PageSheet><Cell N="PageWidth" V="10"/><Cell N="PageHeight" V="8"/></PageSheet><Rel r:id="rId1"/></Page>
</Pages>
""";

        string document = """
<?xml version="1.0" encoding="utf-8"?>
<VisioDocument xmlns="http://schemas.microsoft.com/office/visio/2012/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<DocumentSettings TopPage="0" DefaultTextStyle="0"/>
<StyleSheets><StyleSheet ID="0" NameU="No Style" IsCustomNameU="1"><Cell N="EnableLineProps" V="1"/></StyleSheet></StyleSheets>
</VisioDocument>
""";

        string contentTypes = """
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/visio/document.xml" ContentType="application/vnd.ms-visio.drawing.main+xml"/>
<Override PartName="/visio/pages/pages.xml" ContentType="application/vnd.ms-visio.pages+xml"/>
<Override PartName="/visio/pages/page1.xml" ContentType="application/vnd.ms-visio.page+xml"/>
<Override PartName="/visio/masters/masters.xml" ContentType="application/vnd.ms-visio.masters+xml"/>
</Types>
""";

        string rootRels = """
<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.microsoft.com/visio/2010/relationships/document" Target="visio/document.xml"/>
</Relationships>
""";

        string docRels = """
<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.microsoft.com/visio/2010/relationships/pages" Target="pages/pages.xml"/>
<Relationship Id="rId2" Type="http://schemas.microsoft.com/visio/2010/relationships/masters" Target="masters/masters.xml"/>
</Relationships>
""";

        string pagesRels = """
<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.microsoft.com/visio/2010/relationships/page" Target="page1.xml"/>
</Relationships>
""";

        string emptyRels = """
<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
""";

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        void Add(string entryName, string content)
        {
            var entry = zip.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false));
            writer.Write(content);
        }

        Add("[Content_Types].xml", contentTypes);
        Add("_rels/.rels", rootRels);
        Add("visio/document.xml", document);
        Add("visio/_rels/document.xml.rels", docRels);
        Add("visio/pages/pages.xml", pages);
        Add("visio/pages/_rels/pages.xml.rels", pagesRels);
        Add("visio/pages/page1.xml", page);
        Add("visio/pages/_rels/page1.xml.rels", emptyRels);
        Add("visio/masters/masters.xml",
            """
<?xml version="1.0" encoding="utf-8"?>
<Masters xmlns="http://schemas.microsoft.com/office/visio/2012/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
""");
        Add("visio/masters/_rels/masters.xml.rels", emptyRels);

        return path;
    }

    // ------------------------------------------------------------------
    // Package probes
    // ------------------------------------------------------------------

    internal static XDocument ReadPage(string vsdxPath, int pageIndex = 0)
    {
        using var zip = ZipFile.OpenRead(vsdxPath);
        var entry = zip.GetEntry($"visio/pages/page{pageIndex + 1}.xml");
        Assert.NotNull(entry);
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    internal static int GeometryCount(string vsdxPath) =>
        ReadPage(vsdxPath).Descendants(VisioNs + "Shape")
            .SelectMany(s => s.Elements(VisioNs + "Section"))
            .Count(sec => (string?)sec.Attribute("N") == "Geometry");

    internal static int GeometryRows(string vsdxPath) =>
        ReadPage(vsdxPath).Descendants(VisioNs + "Shape")
            .SelectMany(s => s.Elements(VisioNs + "Section"))
            .Where(sec => (string?)sec.Attribute("N") == "Geometry")
            .Sum(sec => sec.Elements(VisioNs + "Row").Count());

    internal static int ShapeCount(string vsdxPath) =>
        ReadPage(vsdxPath).Descendants(VisioNs + "Shape").Count();

    // ------------------------------------------------------------------
    // A. P0: no geometry injection
    // ------------------------------------------------------------------

    [Fact]
    public void P0_NoOpUpdateDoesNotAddGeometry()
    {
        var dir = TestData.NewTempDir("p0-noop");
        var source = WritePackageWithoutGeometry(dir, "ext.vsdx");
        Assert.Equal(0, GeometryCount(source));

        var engine = new VsdxEngine();
        var model = engine.Read(source).Diagram;
        var output = Path.Combine(dir, "out.vsdx");
        engine.Update(source, model, output);

        Assert.Equal(0, GeometryCount(output));
    }

    [Fact]
    public void P0_AddingNodeLeavesExistingShapesUngeometried()
    {
        var dir = TestData.NewTempDir("p0-add");
        var source = WritePackageWithoutGeometry(dir, "ext.vsdx");

        var engine = new VsdxEngine();
        var model = engine.Read(source).Diagram;
        model.Pages[0].Nodes.Add(new Node
        {
            Id = "added",
            Text = "New",
            Bounds = new Bounds(8, 2.5, 1.8, 0.9),
            Style = new ShapeStyle { Fill = "#70AD47" }
        });

        var output = Path.Combine(dir, "out.vsdx");
        engine.Update(source, model, output);

        var page = ReadPage(output);
        var original = page.Descendants(VisioNs + "Shape")
            .First(s => (string?)s.Attribute("ID") == "1");

        // The pre-existing shape must still have no geometry...
        Assert.DoesNotContain(original.Elements(VisioNs + "Section"),
            sec => (string?)sec.Attribute("N") == "Geometry");

        // ...while the newly created shape legitimately has some.
        Assert.Equal(2, ShapeCount(output));
        Assert.True(GeometryCount(output) >= 1);
    }

    [Fact]
    public void P0_DoesNotStripRealGeometry()
    {
        // A circle carries ~25 geometry rows from its master; the preserver must not touch it.
        var dir = TestData.NewTempDir("p0-keep");
        var source = Path.Combine(dir, "circle.vsdx");

        var engine = new VsdxEngine();
        engine.Create(new Diagram
        {
            Id = "c", Name = "C", Type = "architecture",
            PageSize = new PageSize { Width = 10, Height = 8 },
            Pages =
            {
                new Page
                {
                    Name = "P", Size = new PageSize { Width = 10, Height = 8 },
                    Nodes =
                    {
                        new Node
                        {
                            Id = "circ", Text = "Circle", Shape = "Circle",
                            Bounds = new Bounds(5, 4, 2, 2)
                        }
                    }
                }
            }
        }, source);

        int before = GeometryRows(source);
        Assert.True(before > 5, $"a circle should carry more than a rectangle's 5 rows, got {before}");

        var model = engine.Read(source).Diagram;
        var output = Path.Combine(dir, "out.vsdx");
        engine.Update(source, model, output);

        Assert.Equal(before, GeometryRows(output));
    }

    [Fact]
    public void P0_GeometryPreserverIsIdempotent()
    {
        // Running update twice must not accumulate or lose sections.
        var dir = TestData.NewTempDir("p0-idem");
        var source = WritePackageWithoutGeometry(dir, "ext.vsdx");

        var engine = new VsdxEngine();
        var first = Path.Combine(dir, "a.vsdx");
        engine.Update(source, engine.Read(source).Diagram, first);

        var second = Path.Combine(dir, "b.vsdx");
        engine.Update(first, engine.Read(first).Diagram, second);

        Assert.Equal(0, GeometryCount(first));
        Assert.Equal(0, GeometryCount(second));
        Assert.Equal(ShapeCount(first), ShapeCount(second));
    }

    [Fact]
    public void P0_SnapshotFindsShapesWithoutGeometry()
    {
        var dir = TestData.NewTempDir("p0-snap");
        var source = WritePackageWithoutGeometry(dir, "ext.vsdx");

        var snapshot = VisioGeometryPreserver.SnapshotUngeometriedShapes(source);

        Assert.True(snapshot.ContainsKey(0));
        Assert.Contains("1", snapshot[0]);
    }

    [Fact]
    public void P0_SnapshotIgnoresGeometriedShapes()
    {
        var dir = TestData.NewTempDir("p0-snap2");
        var source = Path.Combine(dir, "c.vsdx");
        new VsdxEngine().Create(new Diagram
        {
            Id = "c", Name = "C", Type = "architecture",
            PageSize = new PageSize { Width = 10, Height = 8 },
            Pages =
            {
                new Page
                {
                    Name = "P", Size = new PageSize { Width = 10, Height = 8 },
                    Nodes = { new Node { Id = "r", Text = "R", Bounds = new Bounds(5, 4, 2, 1) } }
                }
            }
        }, source);

        var snapshot = VisioGeometryPreserver.SnapshotUngeometriedShapes(source);

        Assert.True(snapshot.Count == 0 || !snapshot.ContainsKey(0)
                    || snapshot[0].Count == 0);
    }

    // ------------------------------------------------------------------
    // Risk: if the fix is ever reverted, these must fail loudly.
    // ------------------------------------------------------------------

    [Fact]
    public void P0_SynthesisedRectangleIsWhatTheLibraryProduces()
    {
        // Documents the exact signature the preserver keys on: 5 rows, MoveTo + 4xLineTo.
        // If OfficeIMO changes its default shape, this test tells us the guard went stale.
        var dir = TestData.NewTempDir("p0-sig");
        var source = WritePackageWithoutGeometry(dir, "ext.vsdx");

        // Load+save without our preserver, to observe the raw library behaviour.
        var raw = Path.Combine(dir, "raw.vsdx");
        var doc = OfficeIMO.Visio.VisioDocument.Load(source);
        doc.Save(raw);

        Assert.Equal(1, GeometryCount(raw));
        Assert.Equal(5, GeometryRows(raw));
    }
}
