using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>ReadTests: reading back what the engine wrote, plus model fidelity.</summary>
public sealed class ReadTests
{
    [Fact]
    public void Read_RecoversNodesTextAndBounds()
    {
        var dir = TestData.NewTempDir("read");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path);

        var page = engine.Read(path).Diagram.Pages[0];
        var alpha = page.Nodes.Single(n => n.Id == "n_a");

        Assert.Equal("Alpha", alpha.Text);
        Assert.Equal("Rectangle", alpha.Shape);
        Assert.Equal(2.0, alpha.Bounds.X, 2);
        Assert.Equal(6.0, alpha.Bounds.Y, 2);
        Assert.Equal(1.6, alpha.Bounds.W, 2);
        Assert.Equal(0.8, alpha.Bounds.H, 2);
    }

    [Fact]
    public void Read_RecoversStyles()
    {
        var dir = TestData.NewTempDir("read-style");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path);

        var alpha = engine.Read(path).Diagram.Pages[0].Nodes.Single(n => n.Id == "n_a");
        Assert.Equal("#2E75B6", alpha.Style!.Fill);
        Assert.Equal("#FFFFFF", alpha.Style.TextColor);
        Assert.True(alpha.Style.Bold);
    }

    [Fact]
    public void Read_RecoversCjkText()
    {
        var dir = TestData.NewTempDir("read-cjk");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path);

        var node = engine.Read(path).Diagram.Pages[0].Nodes.Single(n => n.Id == "n_c");
        Assert.Equal("中文节点", node.Text);
    }

    [Fact]
    public void Read_RecoversConnectors()
    {
        var dir = TestData.NewTempDir("read-conn");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path);

        var edges = engine.Read(path).Diagram.Pages[0].Edges;
        var ab = edges.Single(e => e.Id == "e_ab");
        Assert.Equal("n_a", ab.From);
        Assert.Equal("n_b", ab.To);
        Assert.Equal("calls", ab.Label);
    }

    [Fact]
    public void Read_RecoversLayers()
    {
        var dir = TestData.NewTempDir("read-layer");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path);

        var page = engine.Read(path).Diagram.Pages[0];
        Assert.Contains(page.Layers, l => l.Name == "App");
    }

    [Fact]
    public void Read_PreservesPageSize()
    {
        var dir = TestData.NewTempDir("read-size");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path);

        var page = engine.Read(path).Diagram.Pages[0];
        Assert.Equal(11.0, page.Size!.Width, 2);
        Assert.Equal(8.5, page.Size.Height, 2);
    }

    [Fact]
    public void Read_ReportsMissingFile()
    {
        var engine = new VsdxEngine();
        Assert.Throws<FileNotFoundException>(() => engine.Read("/nonexistent/missing.vsdx"));
    }

    [Fact]
    public void Read_ReportsInvalidPackage()
    {
        var dir = TestData.NewTempDir("read-invalid");
        var path = Path.Combine(dir, "bad.vsdx");
        File.WriteAllText(path, "not a zip package");

        var engine = new VsdxEngine();
        Assert.ThrowsAny<Exception>(() => engine.Read(path));

        var validation = engine.Validate(path);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code.StartsWith("zip.", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_RecordsBackendProvenance()
    {
        var dir = TestData.NewTempDir("read-meta");
        var path = Path.Combine(dir, "out.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path);

        var read = engine.Read(path);
        Assert.Equal("OfficeIMO.Visio", read.Backend);
        Assert.False(string.IsNullOrWhiteSpace(read.BackendVersion));
        Assert.NotNull(read.Diagram.Metadata.SourceHash);
        Assert.NotEmpty(read.Diagram.Metadata.History);
    }
}
