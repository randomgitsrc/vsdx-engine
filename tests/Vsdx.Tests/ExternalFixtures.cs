using System.IO.Compression;
using System.Text;

namespace Vsdx.Tests;

/// <summary>
/// Writes VSDX packages by hand at the OPC level, without using OfficeIMO.
///
/// This matters: reading files produced by the same library that writes them proves
/// nothing about external compatibility. These fixtures are byte-level independent of the
/// engine's writer, so ExternalVsdxTests exercise the reader against foreign structure.
/// </summary>
public static class ExternalFixtures
{
    private const string VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";
    private const string RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string RtId = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Basic package: 3 shapes, 1 connector, a stylesheet, no container.</summary>
    public static string WriteHandBuiltPackage(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        Directory.CreateDirectory(dir);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        Write(zip, "[Content_Types].xml", ContentTypes(new[] { "page1.xml" }));
        Write(zip, "_rels/.rels", RootRels());
        Write(zip, "docProps/core.xml", CoreProps("External Basic", "external-fixture"));
        Write(zip, "docProps/app.xml", AppProps());
        Write(zip, "visio/document.xml", DocumentXml());
        Write(zip, "visio/_rels/document.xml.rels", DocumentRels());
        Write(zip, "visio/pages/pages.xml", PagesXml(("page1.xml", "Diagram-1", 0)));
        Write(zip, "visio/pages/_rels/pages.xml.rels", PagesRels(new[] { "page1.xml" }));
        Write(zip, "visio/pages/page1.xml", PageXml(
            width: 11, height: 8.5,
            shapes: new[]
            {
                Rect(1, "外部系统", 2.0, 6.0, 2.0, 0.8, "#2E75B6", "#1F4E79"),
                Rect(2, "Legacy CRM", 5.5, 6.0, 2.0, 0.8, "#70AD47", "#507E32"),
                Rect(3, "Message Queue", 9.0, 3.0, 2.0, 0.8, "#FFC000", "#BF8F00")
            },
            connects: new[] { (7, 1, 2) }));

        return path;
    }

    /// <summary>Richer package: shape data, a container, richer styles.</summary>
    public static string WriteComplexPackage(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        Directory.CreateDirectory(dir);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        Write(zip, "[Content_Types].xml", ContentTypes(new[] { "page1.xml" }));
        Write(zip, "_rels/.rels", RootRels());
        Write(zip, "docProps/core.xml", CoreProps("External Complex", "external-fixture"));
        Write(zip, "docProps/app.xml", AppProps());
        Write(zip, "visio/document.xml", DocumentXml());
        Write(zip, "visio/_rels/document.xml.rels", DocumentRels());
        Write(zip, "visio/pages/pages.xml", PagesXml(("page1.xml", "Orders", 0)));
        Write(zip, "visio/pages/_rels/pages.xml.rels", PagesRels(new[] { "page1.xml" }));
        Write(zip, "visio/pages/page1.xml", ComplexPageXml());

        return path;
    }

    /// <summary>Two-page package for multi-page read/render coverage.</summary>
    public static string WriteMultiPagePackage(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        Directory.CreateDirectory(dir);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        Write(zip, "[Content_Types].xml", ContentTypes(new[] { "page1.xml", "page2.xml" }));
        Write(zip, "_rels/.rels", RootRels());
        Write(zip, "docProps/core.xml", CoreProps("External Multi", "external-fixture"));
        Write(zip, "docProps/app.xml", AppProps());
        Write(zip, "visio/document.xml", DocumentXml());
        Write(zip, "visio/_rels/document.xml.rels", DocumentRels());
        Write(zip, "visio/pages/pages.xml", PagesXml(("page1.xml", "Sheet A", 0), ("page2.xml", "Sheet B", 1)));
        Write(zip, "visio/pages/_rels/pages.xml.rels", PagesRels(new[] { "page1.xml", "page2.xml" }));

        Write(zip, "visio/pages/page1.xml", PageXml(
            width: 8.5, height: 11,
            shapes: new[] { Rect(1, "Sheet A Node", 3.0, 8.0, 2.0, 0.8, "#2E75B6", "#1F4E79") },
            connects: Array.Empty<(int, int, int)>()));

        Write(zip, "visio/pages/page2.xml", PageXml(
            width: 8.5, height: 11,
            shapes: new[] { Rect(1, "Sheet B Node", 3.0, 8.0, 2.0, 0.8, "#C00000", "#7F0000") },
            connects: Array.Empty<(int, int, int)>()));

        return path;
    }

    // ---- part writers ------------------------------------------------------

    private static void Write(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string ContentTypes(string[] pages)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="utf-8"?>""");
        sb.Append("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
        sb.Append("""<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />""");
        sb.Append("""<Default Extension="xml" ContentType="application/xml" />""");
        sb.Append("""<Override PartName="/visio/document.xml" ContentType="application/vnd.ms-visio.drawing.main+xml" />""");
        sb.Append("""<Override PartName="/visio/pages/pages.xml" ContentType="application/vnd.ms-visio.pages+xml" />""");
        sb.Append("""<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml" />""");
        sb.Append("""<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml" />""");
        foreach (var page in pages)
            sb.Append($"""<Override PartName="/visio/pages/{page}" ContentType="application/vnd.ms-visio.page+xml" />""");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string RootRels() =>
        $"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{RelNs}"><Relationship Type="http://schemas.microsoft.com/visio/2010/relationships/document" Target="visio/document.xml" Id="rId1" /><Relationship Type="{RtId}/metadata/core-properties" Target="docProps/core.xml" Id="rId2" /><Relationship Type="{RtId}/extended-properties" Target="docProps/app.xml" Id="rId3" /></Relationships>""";

    private static string DocumentRels() =>
        $"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{RelNs}"><Relationship Type="http://schemas.microsoft.com/visio/2010/relationships/pages" Target="pages/pages.xml" Id="rId1" /></Relationships>""";

    private static string PagesRels(IEnumerable<string> pages)
    {
        var sb = new StringBuilder($"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{RelNs}">""");
        int id = 1;
        foreach (var page in pages)
            sb.Append($"""<Relationship Type="http://schemas.microsoft.com/visio/2010/relationships/page" Target="{page}" Id="rId{id++}" />""");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string PagesXml(params (string file, string name, int id)[] pages)
    {
        var sb = new StringBuilder($"""<?xml version="1.0" encoding="utf-8"?><Pages xmlns="{VisioNs}" xmlns:r="{RtId}">""");
        int relId = 1;
        foreach (var (_, name, id) in pages)
        {
            sb.Append($"""<Page ID="{id}" NameU="{name}" Name="{name}" ViewScale="-1" ViewCenterX="4.25" ViewCenterY="5.5"><PageSheet LineStyle="0" FillStyle="0" TextStyle="0"><Cell N="PageWidth" V="8.5" /><Cell N="PageHeight" V="11" /><Cell N="PageScale" V="1" /><Cell N="DrawingScale" V="1" /></PageSheet><Rel r:id="rId{relId++}" /></Page>""");
        }
        sb.Append("</Pages>");
        return sb.ToString();
    }

    private static string DocumentXml() =>
        $"""<?xml version="1.0" encoding="utf-8"?><VisioDocument xmlns="{VisioNs}" xmlns:r="{RtId}"><DocumentSettings TopPage="0" DefaultTextStyle="0" DefaultLineStyle="0" DefaultFillStyle="0" DefaultGuideStyle="0"><GlueSettings>9</GlueSettings><SnapSettings>65847</SnapSettings></DocumentSettings><Colors/><FaceNames><FaceName NameU="Calibri" UnicodeRanges="-536859905 -1073732485 9 0" CharSets="536871327 0" Panose="2 15 5 2 2 2 4 3 2 4" Flags="325" /></FaceNames><StyleSheets><StyleSheet ID="0" NameU="No Style" IsCustomNameU="1" Name="No Style" IsCustomName="1"><Cell N="EnableLineProps" V="1" /><Cell N="EnableFillProps" V="1" /><Cell N="EnableTextProps" V="1" /><Cell N="HideForApply" V="0" /></StyleSheet></StyleSheets></VisioDocument>""";

    private static string CoreProps(string title, string author) =>
        $"""<?xml version="1.0" encoding="utf-8"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:title>{title}</dc:title><dc:creator>{author}</dc:creator><cp:lastModifiedBy>{author}</cp:lastModifiedBy></cp:coreProperties>""";

    private static string AppProps() =>
        """<?xml version="1.0" encoding="utf-8"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Application>External Fixture Writer</Application></Properties>""";

    private static string Rect(int id, string text, double pinX, double pinY, double w, double h,
                               string fill, string line) =>
        $"""<Shape ID="{id}" NameU="Rectangle" Type="Shape" LineStyle="0" FillStyle="0" TextStyle="0"><Cell N="PinX" V="{pinX}" /><Cell N="PinY" V="{pinY}" /><Cell N="Width" V="{w}" /><Cell N="Height" V="{h}" /><Cell N="LocPinX" V="{w / 2}" /><Cell N="LocPinY" V="{h / 2}" /><Cell N="FillForegnd" V="{fill}" /><Cell N="LineColor" V="{line}" /><Section N="Character"><Row IX="0"><Cell N="Font" V="Calibri" /><Cell N="Size" V="0.1666666666666667" U="PT" /></Row></Section><Text>{text}</Text></Shape>""";

    private static string Connector(int id, string text) =>
        $"""<Shape ID="{id}" NameU="Dynamic connector" Type="Shape" LineStyle="0" FillStyle="0" TextStyle="0"><Cell N="PinX" V="4.5" /><Cell N="PinY" V="4.5" /><Cell N="Width" V="2" /><Cell N="Height" V="0" /><Cell N="BeginX" V="3" /><Cell N="BeginY" V="4.5" /><Cell N="EndX" V="6" /><Cell N="EndY" V="4.5" /><Cell N="EndArrow" V="4" /><Text>{text}</Text></Shape>""";

    private static string PageXml(double width, double height, string[] shapes,
                                  (int id, int from, int to)[] connects)
    {
        var sb = new StringBuilder();
        sb.Append($"""<?xml version="1.0" encoding="utf-8"?><PageContents xmlns="{VisioNs}" xmlns:r="{RtId}">""");
        sb.Append($"""<Shapes>""");
        foreach (var shape in shapes) sb.Append(shape);
        int nextId = shapes.Length + 1;
        foreach (var (id, _, _) in connects) sb.Append(Connector(id, "uses"));
        nextId = Math.Max(nextId, connects.Length == 0 ? 1 : connects.Max(c => c.id) + 1);
        sb.Append("</Shapes>");
        sb.Append("<Connects>");
        foreach (var (id, from, to) in connects)
        {
            sb.Append($"""<Connect FromSheet="{id}" FromCell="BeginX" ToSheet="{from}" ToCell="PinX" />""");
            sb.Append($"""<Connect FromSheet="{id}" FromCell="EndX" ToSheet="{to}" ToCell="PinX" />""");
        }
        sb.Append("</Connects>");
        sb.Append($"<PageSheet LineStyle=\"0\" FillStyle=\"0\" TextStyle=\"0\"><Cell N=\"PageWidth\" V=\"{width}\" /><Cell N=\"PageHeight\" V=\"{height}\" /><Cell N=\"PageScale\" V=\"1\" /><Cell N=\"DrawingScale\" V=\"1\" /></PageSheet>");
        sb.Append("</PageContents>");
        return sb.ToString();
    }

    /// <summary>Complex page: a container shape, shape data rows and varied styling.</summary>
    private static string ComplexPageXml()
    {
        var sb = new StringBuilder();
        sb.Append($"""<?xml version="1.0" encoding="utf-8"?><PageContents xmlns="{VisioNs}" xmlns:r="{RtId}">""");
        sb.Append("<Shapes>");

        // Container shape with the native container markers.
        sb.Append("""<Shape ID="10" NameU="Container" Type="Shape" LineStyle="0" FillStyle="0" TextStyle="0"><Cell N="PinX" V="5.5" /><Cell N="PinY" V="6.6" /><Cell N="Width" V="9" /><Cell N="Height" V="2.4" /><Cell N="LocPinX" V="4.5" /><Cell N="LocPinY" V="1.2" /><Cell N="FillForegnd" V="#EAF3FB" /><Cell N="LineColor" V="#9DC3E6" /><Section N="User"><Row N="msvStructureType"><Cell N="Value" V="Container" /></Row></Section><Text>Orders Platform</Text></Shape>""");

        // Member shapes, one carrying Shape Data.
        sb.Append("""<Shape ID="1" NameU="Rectangle" Type="Shape" LineStyle="0" FillStyle="0" TextStyle="0"><Cell N="PinX" V="3" /><Cell N="PinY" V="6.6" /><Cell N="Width" V="2" /><Cell N="Height" V="0.8" /><Cell N="LocPinX" V="1" /><Cell N="LocPinY" V="0.4" /><Cell N="FillForegnd" V="#2E75B6" /><Cell N="LineColor" V="#1F4E79" /><Section N="Character"><Row IX="0"><Cell N="Font" V="Calibri" /><Cell N="Size" V="0.1666666666666667" U="PT" /></Row></Section><Text>Order API</Text></Shape>""");
        sb.Append("""<Shape ID="2" NameU="Rectangle" Type="Shape" LineStyle="0" FillStyle="0" TextStyle="0"><Cell N="PinX" V="7.5" /><Cell N="PinY" V="6.6" /><Cell N="Width" V="2" /><Cell N="Height" V="0.8" /><Cell N="LocPinX" V="1" /><Cell N="LocPinY" V="0.4" /><Cell N="FillForegnd" V="#70AD47" /><Cell N="LineColor" V="#507E32" /><Section N="Character"><Row IX="0"><Cell N="Font" V="Calibri" /><Cell N="Size" V="0.1666666666666667" U="PT" /></Row></Section><Section N="Property"><Row N="engine"><Cell N="Value" V="postgres" /><Cell N="Label" V="Engine" /><Cell N="Type" V="0" /></Row><Row N="retention"><Cell N="Value" V="30d" /><Cell N="Label" V="Retention" /><Cell N="Type" V="0" /></Row></Section><Text>Orders DB</Text></Shape>""");

        // Two more plain shapes.
        sb.Append(Rect(3, "Billing Worker", 3.0, 3.0, 2.0, 0.8, "#FFC000", "#BF8F00"));
        sb.Append(Rect(4, "Reporting", 7.5, 3.0, 2.0, 0.8, "#7030A0", "#4B1F6F"));

        // A connector between two members.
        sb.Append("""<Shape ID="7" NameU="Dynamic connector" Type="Shape" LineStyle="0" FillStyle="0" TextStyle="0"><Cell N="PinX" V="5.25" /><Cell N="PinY" V="6.6" /><Cell N="Width" V="2.5" /><Cell N="Height" V="0" /><Cell N="BeginX" V="4" /><Cell N="BeginY" V="6.6" /><Cell N="EndX" V="6.5" /><Cell N="EndY" V="6.6" /><Cell N="EndArrow" V="4" /><Text>sql</Text></Shape>""");

        sb.Append("</Shapes>");
        sb.Append("<Connects>");
        sb.Append("""<Connect FromSheet="7" FromCell="BeginX" ToSheet="1" ToCell="PinX" />""");
        sb.Append("""<Connect FromSheet="7" FromCell="EndX" ToSheet="2" ToCell="PinX" />""");
        sb.Append("</Connects>");
        sb.Append("""<PageSheet LineStyle="0" FillStyle="0" TextStyle="0"><Cell N="PageWidth" V="11" /><Cell N="PageHeight" V="8.5" /><Cell N="PageScale" V="1" /><Cell N="DrawingScale" V="1" /></PageSheet>""");
        sb.Append("</PageContents>");
        return sb.ToString();
    }
}
