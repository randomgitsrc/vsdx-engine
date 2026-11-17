using System.IO.Compression;
using System.Xml.Linq;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// ValidationTests: the structural and semantic checks catch real defects, not just
/// "the file opens".
/// </summary>
public sealed class ValidationTests
{
    [Fact]
    public void Validate_AcceptsEngineOutput()
    {
        var dir = TestData.NewTempDir("val-good");
        var path = Path.Combine(dir, "good.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);

        var result = new VsdxEngine().Validate(path);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Code}:{e.Message}")));
        Assert.NotEmpty(result.Checks);
        Assert.All(result.Checks, c => Assert.True(c.Passed, $"{c.Name}: {c.Detail}"));
    }

    [Fact]
    public void Validate_RejectsMissingFile()
    {
        var result = new VsdxEngine().Validate(Path.Combine(TestData.NewTempDir("val-missing"), "nope.vsdx"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "file.missing");
    }

    [Fact]
    public void Validate_RejectsNonZip()
    {
        var dir = TestData.NewTempDir("val-notzip");
        var path = Path.Combine(dir, "x.vsdx");
        File.WriteAllText(path, "this is not a package");

        var result = new VsdxEngine().Validate(path);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "zip.unreadable");
    }

    [Fact]
    public void Validate_RejectsPackageWithoutContentTypes()
    {
        var dir = TestData.NewTempDir("val-noct");
        var path = Path.Combine(dir, "x.vsdx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("visio/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<VisioDocument xmlns=\"http://schemas.microsoft.com/office/visio/2012/main\" />");
        }

        var result = new VsdxEngine().Validate(path);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "opc.content-types-missing");
    }

    [Fact]
    public void Validate_RejectsMalformedXmlPart()
    {
        var dir = TestData.NewTempDir("val-badxml");
        var path = Path.Combine(dir, "x.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);

        // Corrupt one page part while keeping the ZIP intact.
        var broken = Path.Combine(dir, "broken.vsdx");
        File.Copy(path, broken);
        using (var zip = ZipFile.Open(broken, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("visio/pages/page1.xml");
            Assert.NotNull(entry);
            var bytes = new byte[entry!.Length];
            using (var s = entry.Open()) s.ReadExactly(bytes);
            entry.Delete();
            var replacement = zip.CreateEntry("visio/pages/page1.xml");
            using var writer = new StreamWriter(replacement.Open());
            writer.Write("<PageContents><unclosed>");
        }

        var result = new VsdxEngine().Validate(broken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "xml.unparseable");
    }

    [Fact]
    public void Validate_DetectsDuplicateContentTypeOverride()
    {
        var dir = TestData.NewTempDir("val-dupct");
        var path = Path.Combine(dir, "x.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);

        // Reproduce the OfficeIMO 3.6.1 FixFile defect: the same part declared twice with
        // different content types must be reported, not ignored.
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("[Content_Types].xml")!;
            XDocument doc;
            using (var s = entry.Open()) doc = XDocument.Load(s);
            entry.Delete();
            XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
            doc.Root!.Add(new XElement(ns + "Override",
                new XAttribute("PartName", "/visio/pages/pages.xml"),
                new XAttribute("ContentType", "application/vnd.ms-visio.page+xml")));
            var replacement = zip.CreateEntry("[Content_Types].xml");
            using (var s = replacement.Open()) doc.Save(s);
        }

        var result = new VsdxEngine().Validate(path);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "opc.duplicate-override");
    }

    [Fact]
    public void Validate_DetectsBrokenConnectorReference()
    {
        var dir = TestData.NewTempDir("val-connect");
        var path = Path.Combine(dir, "x.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("visio/pages/page1.xml")!;
            XDocument doc;
            using (var s = entry.Open()) doc = XDocument.Load(s);
            entry.Delete();
            XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
            var connect = doc.Descendants(ns + "Connect").First();
            connect.SetAttributeValue("ToSheet", "99999");
            var replacement = zip.CreateEntry("visio/pages/page1.xml");
            using (var s = replacement.Open()) doc.Save(s);
        }

        var result = new VsdxEngine().Validate(path);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code is "visio.connect-to" or "visio.connect-from");
    }

    [Fact]
    public void Validate_DetectsDuplicateShapeIds()
    {
        var dir = TestData.NewTempDir("val-dupid");
        var path = Path.Combine(dir, "x.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("visio/pages/page1.xml")!;
            XDocument doc;
            using (var s = entry.Open()) doc = XDocument.Load(s);
            entry.Delete();
            XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
            var shapes = doc.Descendants(ns + "Shape").Take(2).ToList();
            shapes[1].SetAttributeValue("ID", shapes[0].Attribute("ID")!.Value);
            var replacement = zip.CreateEntry("visio/pages/page1.xml");
            using (var s = replacement.Open()) doc.Save(s);
        }

        var result = new VsdxEngine().Validate(path);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "visio.shape-id-duplicate");
    }

    [Fact]
    public void Validate_RenderSmokeTest()
    {
        var dir = TestData.NewTempDir("val-render");
        var path = Path.Combine(dir, "x.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);

        var result = new VsdxEngine().Validate(path, new ValidationOptions { RenderSmoke = true });
        Assert.Contains(result.Checks, c => c.Name == "render.svg-smoke" && c.Passed);
    }

    [Fact]
    public void Validate_ShallowModeSkipsReload()
    {
        var dir = TestData.NewTempDir("val-shallow");
        var path = Path.Combine(dir, "x.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);

        var result = new VsdxEngine().Validate(path, new ValidationOptions { Deep = false });
        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Checks, c => c.Name == "backend.reload");
    }

    [Fact]
    public void Validate_ReportsChecksIndividually()
    {
        var dir = TestData.NewTempDir("val-checks");
        var path = Path.Combine(dir, "x.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);

        var result = new VsdxEngine().Validate(path);
        foreach (var expected in new[]
                 {
                     "zip.readable", "zip.has-content-types", "xml.parseable",
                     "opc.document-override", "opc.pages-override", "opc.page-overrides",
                     "opc.document-relationship", "opc.pages-relationship",
                     "visio.pages-present", "visio.shape-ids-unique",
                     "visio.connector-references", "backend.reload"
                 })
        {
            Assert.Contains(result.Checks, c => c.Name == expected);
        }
    }
}
