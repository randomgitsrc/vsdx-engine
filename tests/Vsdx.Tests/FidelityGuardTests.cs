using Vsdx.Adapters;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// FidelityGuardTests: the engine must refuse to hand back a package that lost content.
///
/// The accepted decision is explicit — a restricted external-edit capability is acceptable,
/// damaging the original file is not, and a plausible-looking but damaged VSDX must never be
/// emitted silently. Refusing is the correct failure mode.
/// </summary>
public sealed class FidelityGuardTests
{
    /// <summary>A package whose single shape has no geometry section.</summary>
    private static string Ungeometried(string dir) =>
        FidelityCorpus.Write(dir, "src.vsdx",
            new FidelityCorpus.ShapeSpec("1", "Rectangle", "Orders", 5, 5, 2, 1));

    /// <summary>A package whose shape carries real custom geometry.</summary>
    private static string WithZigzag(string dir) =>
        FidelityCorpus.Write(dir, "src.vsdx",
            new FidelityCorpus.ShapeSpec("1", "Rectangle", "Orders", 5, 5, 4, 1.5,
                FidelityCorpus.ZigzagGeometry));

    // ------------------------------------------------------------------
    // Capture / Compare
    // ------------------------------------------------------------------

    [Fact]
    public void CaptureRecordsShapes()
    {
        var dir = TestData.NewTempDir("guard-cap");
        var source = WithZigzag(dir);

        var structure = FidelityGuard.Capture(source);

        Assert.True(structure.ContainsKey(0));
        Assert.True(structure[0].ContainsKey("1"));
        Assert.True(structure[0]["1"].GeometryRows > 0);
    }

    [Fact]
    public void IdenticalPackagesHaveNoLosses()
    {
        var dir = TestData.NewTempDir("guard-same");
        var source = WithZigzag(dir);

        var before = FidelityGuard.Capture(source);
        var after = FidelityGuard.Capture(source);

        Assert.Empty(FidelityGuard.Compare(before, after));
    }

    [Fact]
    public void InjectedGeometryIsDetected()
    {
        var dir = TestData.NewTempDir("guard-inject");
        var source = Ungeometried(dir);
        var before = FidelityGuard.Capture(source);

        // Simulate the P0 defect: a plain round-trip through the backend gives the shape geometry.
        var mutated = Path.Combine(dir, "mutated.vsdx");
        var doc = OfficeIMO.Visio.VisioDocument.Load(source);
        doc.Save(mutated);

        var after = FidelityGuard.Capture(mutated);
        var losses = FidelityGuard.Compare(before, after);

        Assert.Contains(losses, l => l.Kind == FidelityLossKind.GeometryInjected && l.IsFatal);
    }

    [Fact]
    public void ReducedGeometryIsDetected()
    {
        var dir = TestData.NewTempDir("guard-reduce");
        var source = WithZigzag(dir);
        var before = FidelityGuard.Capture(source);

        // Strip the geometry manually to simulate a destructive write.
        var stripped = Path.Combine(dir, "stripped.vsdx");
        File.Copy(source, stripped, overwrite: true);
        StripGeometry(stripped);

        var after = FidelityGuard.Capture(stripped);
        var losses = FidelityGuard.Compare(before, after);

        Assert.Contains(losses, l => l.Kind == FidelityLossKind.GeometryReduced && l.IsFatal);
    }

    [Fact]
    public void LostMasterLinkIsDetected()
    {
        var dir = TestData.NewTempDir("guard-master");
        var source = FidelityCorpus.Write(dir, "src.vsdx",
            new FidelityCorpus.ShapeSpec("1", "Circle", "Circle", 5, 5, 2, 2, Master: "5"));
        var before = FidelityGuard.Capture(source);

        var stripped = Path.Combine(dir, "stripped.vsdx");
        File.Copy(source, stripped, overwrite: true);
        RemoveMasterAttribute(stripped);

        var after = FidelityGuard.Capture(stripped);
        var losses = FidelityGuard.Compare(before, after);

        Assert.Contains(losses, l => l.Kind == FidelityLossKind.MasterLinkLost && l.IsFatal);
    }

    [Fact]
    public void MissingShapeIsReportedButNotFatal()
    {
        // Deletion may be intentional, so it is reported for the caller to judge rather than
        // treated as damage on its own.
        var dir = TestData.NewTempDir("guard-missing");
        var source = WithZigzag(dir);
        var before = FidelityGuard.Capture(source);

        var emptied = Path.Combine(dir, "emptied.vsdx");
        File.Copy(source, emptied, overwrite: true);
        RemoveAllShapes(emptied);

        var after = FidelityGuard.Capture(emptied);
        var losses = FidelityGuard.Compare(before, after);

        var loss = Assert.Single(losses);
        Assert.Equal(FidelityLossKind.ShapeMissing, loss.Kind);
        Assert.False(loss.IsFatal);
    }

    // ------------------------------------------------------------------
    // Real update path must stay clean
    // ------------------------------------------------------------------

    [Fact]
    public void NormalUpdateProducesNoFatalLoss()
    {
        var dir = TestData.NewTempDir("guard-ok");
        var source = WithZigzag(dir);

        var engine = new VsdxEngine();
        var output = Path.Combine(dir, "out.vsdx");
        var result = engine.Update(source, engine.Read(source).Diagram, output);

        Assert.True(File.Exists(output));
        Assert.DoesNotContain(result.Compatibility.Issues, i => i.Code == "fidelity.loss");
    }

    [Fact]
    public void IntentionalDeletionIsNotTreatedAsDamage()
    {
        // Two shapes; delete one from the model. That is an instruction, not corruption.
        var dir = TestData.NewTempDir("guard-del");
        var source = FidelityCorpus.Write(dir, "src.vsdx",
            new FidelityCorpus.ShapeSpec("1", "Rectangle", "Keep", 3, 6, 2, 1,
                FidelityCorpus.RectangleGeometry),
            new FidelityCorpus.ShapeSpec("2", "Rectangle", "Drop", 7, 6, 2, 1,
                FidelityCorpus.RectangleGeometry));

        var engine = new VsdxEngine();
        var model = engine.Read(source).Diagram;
        model.Pages[0].Nodes.RemoveAll(n => n.Text == "Drop");

        var output = Path.Combine(dir, "out.vsdx");
        var result = engine.Update(source, model, output);

        Assert.True(File.Exists(output), "an intentional deletion must still produce output");
        Assert.DoesNotContain(result.Compatibility.Issues, i => i.Code == "fidelity.loss");
    }

    [Fact]
    public void MasterBackedShapeKeepsItsLinkThroughUpdate()
    {
        var dir = TestData.NewTempDir("guard-master-rt");
        var source = FidelityCorpus.Write(dir, "src.vsdx",
            new FidelityCorpus.ShapeSpec("1", "Circle", "Circle", 5, 5, 2, 2, Master: "5"));

        var engine = new VsdxEngine();
        var output = Path.Combine(dir, "out.vsdx");
        var result = engine.Update(source, engine.Read(source).Diagram, output);

        var doc = FidelityTests.ReadPage(output);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
        var shape = doc.Descendants(ns + "Shape").Single();

        Assert.Equal("5", (string?)shape.Attribute("Master"));
        Assert.Contains(result.Compatibility.Issues, i => i.Code == "write.master-restored");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void StripGeometry(string path)
    {
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
        Rewrite(path, doc =>
        {
            foreach (var section in doc.Descendants(ns + "Section")
                         .Where(s => (string?)s.Attribute("N") == "Geometry").ToList())
                section.Remove();
        });
    }

    private static void RemoveMasterAttribute(string path)
    {
        Rewrite(path, doc =>
        {
            foreach (var shape in doc.Descendants().Where(e => e.Attribute("Master") is not null))
                shape.Attribute("Master")!.Remove();
        });
    }

    private static void RemoveAllShapes(string path)
    {
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
        Rewrite(path, doc =>
        {
            foreach (var shape in doc.Descendants(ns + "Shape").ToList()) shape.Remove();
        });
    }

    private static void Rewrite(string path, Action<System.Xml.Linq.XDocument> mutate)
    {
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("visio/pages/page1.xml")!;
            System.Xml.Linq.XDocument doc;
            using (var stream = entry.Open()) doc = System.Xml.Linq.XDocument.Load(stream);

            mutate(doc);

            entry.Delete();
            var replacement = zip.CreateEntry("visio/pages/page1.xml");
            using var writer = new StreamWriter(replacement.Open(), new System.Text.UTF8Encoding(false));
            doc.Save(writer);
        }
    }
}
