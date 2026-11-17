using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>ConnectorTests: creation, modification and endpoint integrity.</summary>
public sealed class ConnectorTests
{
    private static (string dir, string path) Build(string label)
    {
        var dir = TestData.NewTempDir(label);
        var path = Path.Combine(dir, "d.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);
        return (dir, path);
    }

    [Fact]
    public void Connectors_KeepEndpoints()
    {
        var (_, path) = Build("conn-endpoints");
        var edges = new VsdxEngine().Read(path).Diagram.Pages[0].Edges;

        Assert.Equal(2, edges.Count);
        Assert.Equal("n_a", edges.Single(e => e.Id == "e_ab").From);
        Assert.Equal("n_b", edges.Single(e => e.Id == "e_ab").To);
        Assert.Equal("n_c", edges.Single(e => e.Id == "e_bc").To);
    }

    [Fact]
    public void Connectors_KeepArrowheads()
    {
        var (_, path) = Build("conn-arrow");
        var edges = new VsdxEngine().Read(path).Diagram.Pages[0].Edges;
        Assert.All(edges, e => Assert.Equal("triangle", e.EndArrow));
    }

    [Fact]
    public void Connectors_KeepLabels()
    {
        var (_, path) = Build("conn-label");
        var edges = new VsdxEngine().Read(path).Diagram.Pages[0].Edges;
        Assert.Equal("calls", edges.Single(e => e.Id == "e_ab").Label);
        Assert.Equal("reads", edges.Single(e => e.Id == "e_bc").Label);
    }

    [Fact]
    public void Connectors_CanBeAddedIncrementally()
    {
        var (dir, v1) = Build("conn-add");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Edges.Add(new Edge
        {
            Id = "e_ca", From = "n_c", To = "n_a", Kind = "straight",
            Label = "feedback", EndArrow = "triangle", LineColor = "#C00000"
        });

        var v2 = Path.Combine(dir, "v2.vsdx");
        var result = engine.Update(v1, model, v2);
        Assert.Equal(1, result.EdgesAdded);

        var edges = engine.Read(v2).Diagram.Pages[0].Edges;
        Assert.Equal(3, edges.Count);
        Assert.Contains(edges, e => e.Id == "e_ca" && e.From == "n_c" && e.To == "n_a");
    }

    [Fact]
    public void Connectors_CannotReferenceMissingNodes()
    {
        var dir = TestData.NewTempDir("conn-missing");
        var path = Path.Combine(dir, "bad.vsdx");

        var model = TestData.SmallModel();
        model.Pages[0].Edges.Add(new Edge { Id = "e_bad", From = "n_a", To = "does-not-exist", Kind = "straight" });

        var engine = new VsdxEngine();
        var result = engine.Create(model, path);

        Assert.Contains(result.Compatibility.Errors, e => e.Code.Contains("edge-to-missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Connectors_SupportRoutingKinds()
    {
        var dir = TestData.NewTempDir("conn-kinds");
        var path = Path.Combine(dir, "k.vsdx");

        var model = TestData.SmallModel();
        model.Pages[0].Edges.Single(e => e.Id == "e_ab").Kind = "rightAngle";
        model.Pages[0].Edges.Single(e => e.Id == "e_bc").Kind = "straight";

        var engine = new VsdxEngine();
        engine.Create(model, path);

        var edges = engine.Read(path).Diagram.Pages[0].Edges;
        Assert.Contains(edges, e => e.Kind is "rightangle" or "rightAngle");
    }

    [Fact]
    public void Connectors_CanBeDeleted()
    {
        var (dir, v1) = Build("conn-delete");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Edges.RemoveAll(e => e.Id == "e_bc");

        var v2 = Path.Combine(dir, "v2.vsdx");
        var result = engine.Update(v1, model, v2, new UpdateOptions { DeleteMissing = true });

        Assert.Equal(1, result.EdgesDeleted);
        Assert.Single(engine.Read(v2).Diagram.Pages[0].Edges);
    }

    [Fact]
    public void Connectors_CanBeReattached()
    {
        var (dir, v1) = Build("conn-retarget");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        var edge = model.Pages[0].Edges.Single(e => e.Id == "e_bc");
        edge.To = "n_a"; // point it back at Alpha

        var v2 = Path.Combine(dir, "v2.vsdx");
        var result = engine.Update(v1, model, v2);

        Assert.True(result.EdgesModified >= 1);
        var reloaded = engine.Read(v2).Diagram.Pages[0].Edges.Single(e => e.Id == "e_bc");
        Assert.Equal("n_a", reloaded.To);
    }
}
