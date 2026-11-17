using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Vsdx.Adapters;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Builds the external-VSDX corpus used by the fidelity tests.
///
/// Every package here is written by hand as raw OPC, never by our own writer. That is the whole
/// point: if the fixtures came from our writer they would only prove the writer is consistent
/// with itself, not that we can read and preserve files produced elsewhere.
///
/// The corpus deliberately covers the shapes that exposed the Phase 8 P0 defect — geometries
/// absent, present, custom, and master-backed.
/// </summary>
internal static class FidelityCorpus
{
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";

    /// <summary>Shape specifications for a fixture page.</summary>
    internal sealed record ShapeSpec(
        string Id,
        string NameU,
        string Text,
        double PinX, double PinY, double Width, double Height,
        string? GeometryRows = null,
        string? Master = null);

    /// <summary>Writes a package containing exactly the shapes described.</summary>
    internal static string Write(string directory, string fileName, params ShapeSpec[] shapes)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);

        var shapeXml = new StringBuilder();
        foreach (var s in shapes)
        {
            shapeXml.Append($"<Shape ID=\"{s.Id}\" NameU=\"{s.NameU}\" Type=\"Shape\" LineStyle=\"0\" FillStyle=\"0\" TextStyle=\"0\"");
            if (s.Master is not null) shapeXml.Append($" Master=\"{s.Master}\"");
            shapeXml.Append('>');

            shapeXml.Append($"<Cell N=\"PinX\" V=\"{Num(s.PinX)}\"/><Cell N=\"PinY\" V=\"{Num(s.PinY)}\"/>");
            shapeXml.Append($"<Cell N=\"Width\" V=\"{Num(s.Width)}\"/><Cell N=\"Height\" V=\"{Num(s.Height)}\"/>");
            shapeXml.Append($"<Cell N=\"LocPinX\" V=\"{Num(s.Width / 2)}\"/><Cell N=\"LocPinY\" V=\"{Num(s.Height / 2)}\"/>");
            shapeXml.Append("<Cell N=\"FillForegnd\" V=\"#4472C4\"/><Cell N=\"LineColor\" V=\"#1F4E79\"/>");

            if (s.GeometryRows is not null) shapeXml.Append(s.GeometryRows);
            if (!string.IsNullOrEmpty(s.Text)) shapeXml.Append($"<Text>{s.Text}</Text>");

            shapeXml.Append("</Shape>");
        }

        string page = $"""
<?xml version="1.0" encoding="utf-8"?>
<PageContents xmlns="http://schemas.microsoft.com/office/visio/2012/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<Shapes>{shapeXml}</Shapes>
</PageContents>
""";

        WritePackage(path, page, shapes.Length);
        return path;
    }

    /// <summary>The signature OfficeIMO synthesises: one MoveTo and four LineTo rows.</summary>
    internal static string RectangleGeometry =>
        "<Section N=\"Geometry\" IX=\"0\">" +
        "<Row T=\"MoveTo\" IX=\"1\"><Cell N=\"X\" V=\"0\"/><Cell N=\"Y\" V=\"0\"/></Row>" +
        "<Row T=\"LineTo\" IX=\"2\"><Cell N=\"X\" V=\"2\"/><Cell N=\"Y\" V=\"0\"/></Row>" +
        "<Row T=\"LineTo\" IX=\"3\"><Cell N=\"X\" V=\"2\"/><Cell N=\"Y\" V=\"1\"/></Row>" +
        "<Row T=\"LineTo\" IX=\"4\"><Cell N=\"X\" V=\"0\"/><Cell N=\"Y\" V=\"1\"/></Row>" +
        "<Row T=\"LineTo\" IX=\"5\"><Cell N=\"X\" V=\"0\"/><Cell N=\"Y\" V=\"0\"/></Row>" +
        "</Section>";

    /// <summary>A hand-drawn zigzag: not a rectangle, must never be stripped or flattened.</summary>
    internal static string ZigzagGeometry =>
        "<Section N=\"Geometry\" IX=\"0\">" +
        "<Cell N=\"NoFill\" V=\"1\"/>" +
        "<Row T=\"MoveTo\" IX=\"1\"><Cell N=\"X\" V=\"0\"/><Cell N=\"Y\" V=\"0\"/></Row>" +
        "<Row T=\"LineTo\" IX=\"2\"><Cell N=\"X\" V=\"1.33\"/><Cell N=\"Y\" V=\"1\"/></Row>" +
        "<Row T=\"LineTo\" IX=\"3\"><Cell N=\"X\" V=\"2.66\"/><Cell N=\"Y\" V=\"0\"/></Row>" +
        "<Row T=\"LineTo\" IX=\"4\"><Cell N=\"X\" V=\"4\"/><Cell N=\"Y\" V=\"1\"/></Row>" +
        "</Section>";

    /// <summary>An arc: an instruction the engine does not interpret and must preserve verbatim.</summary>
    internal static string ArcGeometry =>
        "<Section N=\"Geometry\" IX=\"0\">" +
        "<Cell N=\"NoFill\" V=\"1\"/>" +
        "<Row T=\"MoveTo\" IX=\"1\"><Cell N=\"X\" V=\"0\"/><Cell N=\"Y\" V=\"0\"/></Row>" +
        "<Row T=\"ArcTo\" IX=\"2\"><Cell N=\"X\" V=\"3\"/><Cell N=\"Y\" V=\"1.2\"/>" +
        "<Cell N=\"A\" V=\"1.5\"/><Cell N=\"B\" V=\"0.6\"/></Row>" +
        "</Section>";

    private static void WritePackage(string path, string pageXml, int shapeCount)
    {
        string pages = """
<?xml version="1.0" encoding="utf-8"?>
<Pages xmlns="http://schemas.microsoft.com/office/visio/2012/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<Page ID="0" NameU="Page-1" Name="Page-1"><PageSheet><Cell N="PageWidth" V="12"/><Cell N="PageHeight" V="9"/></PageSheet><Rel r:id="rId1"/></Page>
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

        string masters = """
<?xml version="1.0" encoding="utf-8"?>
<Masters xmlns="http://schemas.microsoft.com/office/visio/2012/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
""";

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        void Add(string name, string content)
        {
            var entry = zip.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        Add("[Content_Types].xml", contentTypes);
        Add("_rels/.rels", rootRels);
        Add("visio/document.xml", document);
        Add("visio/_rels/document.xml.rels", docRels);
        Add("visio/pages/pages.xml", pages);
        Add("visio/pages/_rels/pages.xml.rels", pagesRels);
        Add("visio/pages/page1.xml", pageXml);
        Add("visio/pages/_rels/page1.xml.rels", emptyRels);
        Add("visio/masters/masters.xml", masters);
        Add("visio/masters/_rels/masters.xml.rels", emptyRels);
    }

    private static string Num(double v) =>
        Math.Round(v, 6).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Runs each corpus fixture through update and asserts the geometry section count is unchanged.
/// This is the machine-checkable form of the P0 acceptance criterion.
/// </summary>
public sealed class FidelityCorpusTests
{
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";

    private static Dictionary<string, int> GeometryRowsById(string path)
    {
        var doc = FidelityTests.ReadPage(path);
        return doc.Descendants(VisioNs + "Shape")
            .Where(s => (string?)s.Attribute("ID") is not null)
            .ToDictionary(
                s => (string)s.Attribute("ID")!,
                s => s.Elements(VisioNs + "Section")
                    .Where(sec => (string?)sec.Attribute("N") == "Geometry")
                    .Sum(sec => sec.Elements(VisioNs + "Row").Count()),
                StringComparer.Ordinal);
    }

    /// <summary>Fixtures, each exercising a different geometry situation.</summary>
    public static TheoryData<string> Cases() => new()
    {
        "no-geometry",
        "with-rectangle",
        "with-zigzag",
        "with-arc",
        "master-backed",
        "mixed"
    };

    private static string BuildCase(string dir, string name)
    {
        return name switch
        {
            "no-geometry" => FidelityCorpus.Write(dir, "no-geometry.vsdx",
                new FidelityCorpus.ShapeSpec("1", "Rectangle", "No geom", 3, 6, 2, 1)),

            "with-rectangle" => FidelityCorpus.Write(dir, "with-rectangle.vsdx",
                new FidelityCorpus.ShapeSpec("1", "Rectangle", "Rect", 3, 6, 2, 1,
                    FidelityCorpus.RectangleGeometry)),

            "with-zigzag" => FidelityCorpus.Write(dir, "with-zigzag.vsdx",
                new FidelityCorpus.ShapeSpec("1", "FreeForm", "Zig", 3, 6, 4, 1.5,
                    FidelityCorpus.ZigzagGeometry)),

            "with-arc" => FidelityCorpus.Write(dir, "with-arc.vsdx",
                new FidelityCorpus.ShapeSpec("1", "FreeForm", "Arc", 3, 6, 3, 1.2,
                    FidelityCorpus.ArcGeometry)),

            "master-backed" => FidelityCorpus.Write(dir, "master-backed.vsdx",
                new FidelityCorpus.ShapeSpec("1", "Circle", "Circle", 3, 6, 2, 2, Master: "5")),

            "mixed" => FidelityCorpus.Write(dir, "mixed.vsdx",
                new FidelityCorpus.ShapeSpec("1", "Rectangle", "No geom", 2, 7, 2, 1),
                new FidelityCorpus.ShapeSpec("2", "Rectangle", "Rect", 5, 7, 2, 1,
                    FidelityCorpus.RectangleGeometry),
                new FidelityCorpus.ShapeSpec("3", "FreeForm", "Zig", 8, 7, 3, 1.2,
                    FidelityCorpus.ZigzagGeometry),
                new FidelityCorpus.ShapeSpec("4", "Circle", "Circle", 2, 4, 2, 2, Master: "5")),

            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void UpdatePreservesGeometrySectionCounts(string caseName)
    {
        var dir = TestData.NewTempDir($"fid-{caseName}");
        var source = BuildCase(dir, caseName);

        var before = GeometryRowsById(source);

        // Which shapes referenced a master in the original?
        var originalMasters = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var shape in FidelityTests.ReadPage(source)
                     .Descendants(System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/office/visio/2012/main") + "Shape"))
        {
            var sid = (string?)shape.Attribute("ID");
            if (sid is null) continue;
            originalMasters[sid] = shape.Attribute("Master") is not null;
        }

        var engine = new VsdxEngine();
        var output = Path.Combine(dir, "out.vsdx");
        engine.Update(source, engine.Read(source).Diagram, output);

        var after = GeometryRowsById(output);

        // A shape that already had geometry must keep exactly those rows.
        // A shape with *no* geometry and no master must stay that way (the P0 defect was that it
        // silently gained a synthesised rectangle).
        // A master-backed shape is the one legitimate exception: the backend materialises the
        // master's outline on save, which is faithful rather than destructive.
        foreach (var (id, rows) in before)
        {
            if (!after.TryGetValue(id, out int now)) continue;

            if (rows > 0)
            {
                Assert.Equal(rows, now);
                continue;
            }

            if (now == 0) continue;   // stayed ungeometried: correct

            // Gained geometry: only acceptable if the shape references a master.
            var doc = FidelityTests.ReadPage(output);
            System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
            var shape = doc.Descendants(ns + "Shape")
                .First(s => (string?)s.Attribute("ID") == id);

            // A shape that was master-backed must still be master-backed: losing the link turns it
            // into a detached outline, so editing the master in Visio would no longer update it.
            // VisioMasterBridge restores the reference from Node.SourceMaster.
            bool hasMaster = shape.Attribute("Master") is not null;
            bool wasMasterBacked = originalMasters.GetValueOrDefault(id);

            Assert.True(hasMaster,
                $"shape {id} gained {now} geometry rows but lost its master reference " +
                $"(was master-backed: {wasMasterBacked})");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void UpdatePreservesShapeCount(string caseName)
    {
        var dir = TestData.NewTempDir($"fidcount-{caseName}");
        var source = BuildCase(dir, caseName);

        var engine = new VsdxEngine();
        var output = Path.Combine(dir, "out.vsdx");
        engine.Update(source, engine.Read(source).Diagram, output);

        Assert.Equal(FidelityTests.ShapeCount(source), FidelityTests.ShapeCount(output));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CustomGeometryIsNeverStripped(string caseName)
    {
        if (caseName is not ("with-zigzag" or "with-arc" or "mixed")) return;

        var dir = TestData.NewTempDir($"fidkeep-{caseName}");
        var source = BuildCase(dir, caseName);

        var engine = new VsdxEngine();
        var output = Path.Combine(dir, "out.vsdx");
        engine.Update(source, engine.Read(source).Diagram, output);

        // Custom rows must survive: stripping them turns a drawn shape into nothing.
        var original = GeometryRowsById(source).Values.Where(v => v > 0).Sum();
        var preserved = GeometryRowsById(output).Values.Where(v => v > 0).Sum();

        Assert.True(preserved >= original,
            $"{caseName}: {preserved} geometry rows remain, expected at least {original}");
    }
}
