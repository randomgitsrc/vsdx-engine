using System.IO.Compression;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>GroupTests: real Visio containers, membership and containment.</summary>
public sealed class GroupTests
{
    [Fact]
    public void Group_IsWrittenAsNativeContainer()
    {
        var dir = TestData.NewTempDir("group-native");
        var path = Path.Combine(dir, "g.vsdx");
        new VsdxEngine().Create(TestData.SmallModel(), path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.Entries.First(e => e.FullName == "visio/pages/page1.xml").Open());
        var xml = reader.ReadToEnd();

        Assert.Contains("msvStructureType", xml);
        Assert.Contains("Container", xml);
        Assert.Contains("Layer One", xml);
    }

    [Fact]
    public void Group_KeepsMembership()
    {
        var dir = TestData.NewTempDir("group-members");
        var path = Path.Combine(dir, "g.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), path);

        var page = engine.Read(path).Diagram.Pages[0];
        var group = page.Groups.Single();
        Assert.Contains("n_a", group.Members);
        Assert.Contains("n_b", group.Members);
        Assert.DoesNotContain("n_c", group.Members);
    }

    [Fact]
    public void Group_BoundsEncloseMembers()
    {
        var dir = TestData.NewTempDir("group-bounds");
        var path = Path.Combine(dir, "g.vsdx");
        var engine = new VsdxEngine();

        var model = TestData.SmallModel();
        var result = engine.Create(model, path);
        Assert.Empty(result.Compatibility.Errors);

        var page = ForLayout(model);
        var problems = DiagramLayout.CheckContainment(page);
        Assert.Empty(problems);
    }

    [Fact]
    public void Group_AutosizesFromMembers()
    {
        var dir = TestData.NewTempDir("group-autosize");
        var path = Path.Combine(dir, "g.vsdx");
        var engine = new VsdxEngine();

        var model = TestData.SmallModel();
        engine.Create(model, path);

        var group = engine.Read(path).Diagram.Pages[0].Groups.Single();
        Assert.NotNull(group.Bounds);

        var members = engine.Read(path).Diagram.Pages[0].Nodes.Where(n => group.Members.Contains(n.Id)).ToList();
        double memberMaxX = members.Max(n => n.Bounds.X + n.Bounds.W / 2);
        Assert.True(group.Bounds!.X + group.Bounds.W / 2 >= memberMaxX,
            "container must extend past its rightmost member");
    }

    [Fact]
    public void Group_MembershipChangesIncrementally()
    {
        var dir = TestData.NewTempDir("group-edit");
        var v1 = Path.Combine(dir, "v1.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), v1);

        var model = engine.Read(v1).Diagram;
        var group = model.Pages[0].Groups.Single();
        group.Members.Add("n_c");
        model.Pages[0].Nodes.Single(n => n.Id == "n_c").GroupId = group.Id;

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var reloaded = engine.Read(v2).Diagram.Pages[0].Groups.Single();
        Assert.Contains("n_c", reloaded.Members);
    }

    [Fact]
    public void Group_SupportsMultipleGroups()
    {
        var dir = TestData.NewTempDir("group-multi");
        var path = Path.Combine(dir, "g.vsdx");

        var model = TestData.SmallModel();
        model.Pages[0].Groups.Add(new Group
        {
            Id = "grp_2",
            Name = "Second Band",
            Kind = "container",
            AutoSize = true
        });
        model.Pages[0].Nodes.Single(n => n.Id == "n_c").GroupId = "grp_2";
        model.Pages[0].Groups.Single(g => g.Id == "grp_2").Members.Add("n_c");

        var engine = new VsdxEngine();
        var result = engine.Create(model, path);
        Assert.Empty(result.Compatibility.Errors);

        var groups = engine.Read(path).Diagram.Pages[0].Groups;
        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void Group_DetectsMemberEscapingContainer()
    {
        var error = DiagramLayout.CheckContainment(new Page
        {
            Name = "p",
            Groups = { new Group { Id = "g", Members = { "a" }, Bounds = new Bounds(1, 1, 1, 1) } },
            Nodes = { new Node { Id = "a", Bounds = new Bounds(5, 5, 1, 1) } }
        });

        Assert.NotEmpty(error);
        Assert.Contains("escapes", error[0]);
    }

    /// <summary>Runs the layout pass so group bounds are populated before assertion.</summary>
    private static Page ForLayout(Diagram model)
    {
        DiagramLayout.Apply(model);
        return model.Pages[0];
    }
}
