using System.IO.Compression;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// ExternalVsdxTests: packages the engine did not author. Two fixture shapes are used:
/// one built by hand at the OPC level (independent of OfficeIMO) and one with extra
/// native features (shape data, container, multi-page) to probe preservation.
/// </summary>
public sealed class ExternalVsdxTests
{
    [Fact]
    public void External_ReadsHandBuiltPackage()
    {
        var dir = TestData.NewTempDir("ext-hand");
        var path = ExternalFixtures.WriteHandBuiltPackage(dir, "external-basic.vsdx");

        var engine = new VsdxEngine();
        var read = engine.Read(path);

        var page = read.Diagram.Pages[0];
        Assert.Equal(3, page.Nodes.Count);
        Assert.Equal(1, page.Edges.Count);
        Assert.Contains(page.Nodes, n => n.Text == "外部系统");
        Assert.Contains(page.Nodes, n => n.Text == "Legacy CRM");
    }

    [Fact]
    public void External_ExportsModel()
    {
        var dir = TestData.NewTempDir("ext-export");
        var path = ExternalFixtures.WriteHandBuiltPackage(dir, "external-basic.vsdx");
        var json = Path.Combine(dir, "external.json");

        var engine = new VsdxEngine();
        engine.ExportModel(path, json);

        Assert.True(File.Exists(json));
        var model = DiagramJson.Load(json);
        Assert.Equal(3, model.Pages[0].Nodes.Count);
    }

    [Fact]
    public void External_ModifyOneObjectAndSave()
    {
        var dir = TestData.NewTempDir("ext-modify");
        var path = ExternalFixtures.WriteHandBuiltPackage(dir, "external-basic.vsdx");
        var engine = new VsdxEngine();

        var model = engine.Read(path).Diagram;
        var target = model.Pages[0].Nodes.First(n => n.Text == "Legacy CRM");
        target.Text = "Legacy CRM (retired)";
        target.Style ??= new ShapeStyle();
        target.Style.Fill = "#C00000";

        var output = Path.Combine(dir, "external-modified.vsdx");
        var result = engine.Update(path, model, output);

        Assert.Equal(1, result.NodesModified);
        Assert.Equal(0, result.NodesAdded);
        Assert.Equal(0, result.NodesDeleted);
        Assert.True(result.NodesPreserved >= 2, $"other nodes must be preserved, got {result.NodesPreserved}");
        Assert.Empty(result.Compatibility.Errors);
    }

    [Fact]
    public void External_ReReadAfterModify()
    {
        var dir = TestData.NewTempDir("ext-reread");
        var path = ExternalFixtures.WriteHandBuiltPackage(dir, "external-basic.vsdx");
        var engine = new VsdxEngine();

        var model = engine.Read(path).Diagram;
        model.Pages[0].Nodes.First(n => n.Text == "Legacy CRM").Text = "Renamed";
        var output = Path.Combine(dir, "out.vsdx");
        engine.Update(path, model, output);

        var reread = engine.Read(output).Diagram.Pages[0];
        Assert.Equal(3, reread.Nodes.Count);
        Assert.Contains(reread.Nodes, n => n.Text == "Renamed");
        Assert.Contains(reread.Nodes, n => n.Text == "外部系统");
    }

    [Fact]
    public void External_Renders()
    {
        var dir = TestData.NewTempDir("ext-render");
        var path = ExternalFixtures.WriteHandBuiltPackage(dir, "external-basic.vsdx");

        var engine = new VsdxEngine();
        var svg = Path.Combine(dir, "out.svg");
        var result = engine.Render(new RenderRequest { InputPath = path, OutputPath = svg, Format = "svg" });

        Assert.Single(result.Pages);
        var text = File.ReadAllText(svg);
        Assert.Contains("<svg", text);
        Assert.Contains("Legacy CRM", text);
    }

    [Fact]
    public void External_ValidatesCleanly()
    {
        var dir = TestData.NewTempDir("ext-validate");
        var path = ExternalFixtures.WriteHandBuiltPackage(dir, "external-basic.vsdx");

        var result = new VsdxEngine().Validate(path);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Code + ":" + e.Message)));
    }

    [Fact]
    public void External_ComplexPackagePreservesNativeFeatures()
    {
        var dir = TestData.NewTempDir("ext-complex");
        var path = ExternalFixtures.WriteComplexPackage(dir, "external-complex.vsdx");

        var engine = new VsdxEngine();
        var read = engine.Read(path);
        var page = read.Diagram.Pages[0];

        Assert.Equal(4, page.Nodes.Count);
        Assert.Single(page.Groups);

        var db = page.Nodes.Single(n => n.Text == "Orders DB");
        Assert.NotNull(db.ShapeData);
        Assert.Equal("postgres", db.ShapeData!["engine"]);

        // Modify only the title, everything else must survive.
        page.Nodes.Single(n => n.Text == "Order API").Text = "Order API v2";
        var output = Path.Combine(dir, "out.vsdx");
        var result = engine.Update(path, pageMode(read.Diagram), output);

        var reread = engine.Read(output).Diagram.Pages[0];
        Assert.Contains(reread.Nodes, n => n.Text == "Order API v2");
        Assert.Equal("postgres", reread.Nodes.Single(n => n.Text == "Orders DB").ShapeData!["engine"]);
        Assert.Single(reread.Groups);
        Assert.Empty(result.Compatibility.Errors);
    }

    [Fact]
    public void External_MultiPagePackageIsReadable()
    {
        var dir = TestData.NewTempDir("ext-multipage");
        var path = ExternalFixtures.WriteMultiPagePackage(dir, "external-multipage.vsdx");

        var page = new VsdxEngine().Read(path).Diagram.Pages;
        Assert.Equal(2, page.Count);
        Assert.Contains(page, p => p.Name == "Sheet A");
        Assert.Contains(page, p => p.Name == "Sheet B");
    }

    [Fact]
    public void External_MultiPageRendersAllPages()
    {
        var dir = TestData.NewTempDir("ext-multipage-render");
        var path = ExternalFixtures.WriteMultiPagePackage(dir, "external-multipage.vsdx");

        var result = new VsdxEngine().Render(new RenderRequest
        {
            InputPath = path,
            OutputPath = Path.Combine(dir, "sheet.png"),
            Format = "png"
        });

        Assert.Equal(2, result.Pages.Count);
        Assert.All(result.Pages, p => Assert.True(File.Exists(p.OutputPath)));
    }

    private static Diagram pageMode(Diagram d) => d;
}
