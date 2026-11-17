using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>EditTests: incremental modification of an existing package.</summary>
public sealed class EditTests
{
    private static (string dir, string v1) CreateBase(string label)
    {
        var dir = TestData.NewTempDir(label);
        var v1 = Path.Combine(dir, "v1.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), v1);
        return (dir, v1);
    }

    [Fact]
    public void Update_RenamesTextInPlace()
    {
        var (dir, v1) = CreateBase("edit-rename");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        var node = model.Pages[0].Nodes.Single(n => n.Id == "n_a");
        node.Text = "Alpha Renamed";

        var v2 = Path.Combine(dir, "v2.vsdx");
        var result = engine.Update(v1, model, v2);

        Assert.Equal(1, result.NodesModified);
        Assert.Equal(0, result.NodesAdded);
        Assert.Equal(0, result.NodesDeleted);

        var reloaded = engine.Read(v2).Diagram.Pages[0].Nodes.Single(n => n.Id == "n_a");
        Assert.Equal("Alpha Renamed", reloaded.Text);
    }

    [Fact]
    public void Update_AddsNode()
    {
        var (dir, v1) = CreateBase("edit-add");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Nodes.Add(new Node
        {
            Id = "n_new",
            Text = "新增节点",
            Shape = "Hexagon",
            Bounds = new Bounds(5.0, 3.0, 1.8, 0.9),
            Style = new ShapeStyle { Fill = "#7030A0", TextColor = "#FFFFFF" }
        });

        var v2 = Path.Combine(dir, "v2.vsdx");
        var result = engine.Update(v1, model, v2);
        Assert.Equal(1, result.NodesAdded);

        var nodes = engine.Read(v2).Diagram.Pages[0].Nodes;
        Assert.Contains(nodes, n => n.Id == "n_new" && n.Text == "新增节点");
        Assert.Equal(4, nodes.Count);
    }

    [Fact]
    public void Update_DeletesNodeWhenRequested()
    {
        var (dir, v1) = CreateBase("edit-delete");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Nodes.RemoveAll(n => n.Id == "n_c");
        model.Pages[0].Edges.RemoveAll(e => e.From == "n_c" || e.To == "n_c");

        var v2 = Path.Combine(dir, "v2.vsdx");
        var result = engine.Update(v1, model, v2, new UpdateOptions { DeleteMissing = true });

        Assert.Equal(1, result.NodesDeleted);
        Assert.DoesNotContain(engine.Read(v2).Diagram.Pages[0].Nodes, n => n.Id == "n_c");
    }

    [Fact]
    public void Update_MovesAndResizesNode()
    {
        var (dir, v1) = CreateBase("edit-move");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        var node = model.Pages[0].Nodes.Single(n => n.Id == "n_b");
        node.Bounds = new Bounds(7.5, 2.5, 2.0, 1.2);

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var reloaded = engine.Read(v2).Diagram.Pages[0].Nodes.Single(n => n.Id == "n_b");
        Assert.Equal(7.5, reloaded.Bounds.X, 2);
        Assert.Equal(2.5, reloaded.Bounds.Y, 2);
        Assert.Equal(2.0, reloaded.Bounds.W, 2);
        Assert.Equal(1.2, reloaded.Bounds.H, 2);
    }

    [Fact]
    public void Update_ChangesColours()
    {
        var (dir, v1) = CreateBase("edit-colour");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Nodes.Single(n => n.Id == "n_b").Style!.Fill = "#C00000";

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var reloaded = engine.Read(v2).Diagram.Pages[0].Nodes.Single(n => n.Id == "n_b");
        Assert.Equal("#C00000", reloaded.Style!.Fill);
    }

    [Fact]
    public void Update_ChangesShapeData()
    {
        var (dir, v1) = CreateBase("edit-data");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Nodes.Single(n => n.Id == "n_a").ShapeData!["owner"] = "team-z";

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var reloaded = engine.Read(v2).Diagram.Pages[0].Nodes.Single(n => n.Id == "n_a");
        Assert.Equal("team-z", reloaded.ShapeData!["owner"]);
    }

    [Fact]
    public void Update_PreservesUntouchedNodes()
    {
        var (dir, v1) = CreateBase("edit-preserve");
        var engine = new VsdxEngine();

        var before = engine.Read(v1).Diagram.Pages[0].Nodes
            .ToDictionary(n => n.Id, n => (n.Text, n.Bounds.X, n.Style?.Fill));

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Nodes.Single(n => n.Id == "n_a").Text = "changed";
        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var after = engine.Read(v2).Diagram.Pages[0].Nodes.ToDictionary(n => n.Id, n => n);
        foreach (var (id, expected) in before)
        {
            if (id == "n_a") continue;
            Assert.Equal(expected.Text, after[id].Text);
            Assert.Equal(expected.X, after[id].Bounds.X, 2);
        }
    }

    [Fact]
    public void Update_ResizesPage()
    {
        var (dir, v1) = CreateBase("edit-pagesize");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Size = new PageSize { Width = 17, Height = 11, Unit = "inches" };
        model.PageSize = model.Pages[0].Size;

        var v2 = Path.Combine(dir, "v2.vsdx");
        var result = engine.Update(v1, model, v2);
        Assert.Contains(result.Changes, c => c.Kind == "page.resized");

        var page = engine.Read(v2).Diagram.Pages[0];
        Assert.Equal(17.0, page.Size!.Width, 2);
        Assert.Equal(11.0, page.Size.Height, 2);
    }

    [Fact]
    public void Update_RenamesGroup()
    {
        var (dir, v1) = CreateBase("edit-group-rename");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Groups.Single().Name = "Renamed Layer";

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        Assert.Equal("Renamed Layer", engine.Read(v2).Diagram.Pages[0].Groups.Single().Name);
    }

    [Fact]
    public void Update_RecordsChangeHistory()
    {
        var (dir, v1) = CreateBase("edit-history");
        var engine = new VsdxEngine();

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Nodes.Single(n => n.Id == "n_a").Text = "x";
        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var reloaded = engine.Read(v2).Diagram;
        Assert.Contains(reloaded.Metadata.History, h => h.Action == "create");
        Assert.Contains(reloaded.Metadata.History, h => h.Action == "update");
    }
}
