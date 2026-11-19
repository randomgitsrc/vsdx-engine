using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The container formula and the constants it is built from, which `docs/diagram-knowledge/container-geometry.md`
/// publishes for anyone hand-placing nodes.
///
/// The independent agent hit ten `container.heading-collision` warnings because nothing told it that a container
/// reserves 0.46" of heading plus 0.12" of clearance at the top, and that its members therefore cannot be
/// centred — a centred member sits 0.18" too high and lands in the heading band.
///
/// These tests hold both halves: the constants still have the values the document quotes, and the derived
/// height still comes out as `member + 0.80`.
/// </summary>
public sealed class ContainerGeometryTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void TheDocumentedConstantsMatchTheSource()
    {
        var source = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Engine", "DiagramLayout.cs"));

        Assert.Matches(@"ContainerHeading = 0\.46", source);
        Assert.Matches(@"HeadingClearance = 0\.12", source);
        Assert.Matches(@"ContainerPaddingBottom = 0\.22", source);
        Assert.Matches(@"ContainerPaddingX = 0\.30", source);
    }

    [Fact]
    public void TheDocumentQuotesTheSameValues()
    {
        var doc = File.ReadAllText(
            Path.Combine(Root(), "docs", "diagram-knowledge", "container-geometry.md"));

        foreach (var value in new[] { "0.46", "0.12", "0.22", "0.30" })
            Assert.Contains(value, doc);
    }

    [Theory]
    [InlineData(0.6)]
    [InlineData(1.4)]
    [InlineData(2.5)]
    public void AContainerIsItsTallestMemberPlusEightyHundredths(double memberHeight)
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };
        page.Groups.Add(new Group { Id = "g", Name = "带", Kind = "layer", Members = { "a" } });
        page.Nodes.Add(new Node
        {
            Id = "a", Text = "A", GroupId = "g",
            Bounds = new Bounds(6, 5, 1.5, memberHeight)
        });

        DiagramLayout.Apply(new Diagram { Id = "t", Pages = { page } });

        double expected = memberHeight
                        + DiagramLayout.ContainerHeading
                        + DiagramLayout.HeadingClearance
                        + DiagramLayout.ContainerPaddingBottom;

        Assert.Equal(expected, page.Groups[0].Bounds!.H, 3);
    }

    [Fact]
    public void TheTallestMemberSitsFiftyEightHundredthsBelowTheContainerTop()
    {
        // The number a hand-placing author needs: heading plus clearance, not half the leftover space.
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };
        page.Groups.Add(new Group { Id = "g", Name = "带", Kind = "layer", Members = { "a" } });
        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "g", Bounds = new Bounds(6, 5, 1.5, 0.6) });

        DiagramLayout.Apply(new Diagram { Id = "t", Pages = { page } });

        var g = page.Groups[0].Bounds!;
        var n = page.Nodes[0].Bounds!;

        Assert.Equal(0.58, (g.Y + g.H / 2) - (n.Y + n.H / 2), 3);
    }

    [Fact]
    public void MembersAreCentredOnEachOtherButNotOnTheContainer()
    {
        // Documented: mixed heights share a centre line, and that line sits below the container's centre.
        // My first version of this test asserted bottom alignment; the measurement disagreed, and the document
        // was wrong too.
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };
        page.Groups.Add(new Group { Id = "g", Name = "带", Kind = "layer", Members = { "a", "b" } });
        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "g", Bounds = new Bounds(3, 5, 1.5, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "g", Bounds = new Bounds(8, 5, 1.5, 1.4) });

        DiagramLayout.Apply(new Diagram { Id = "t", Pages = { page } });

        var a = page.Nodes.Single(n => n.Id == "a").Bounds!;
        var b = page.Nodes.Single(n => n.Id == "b").Bounds!;

        Assert.Equal(b.Y, a.Y, 3);                        // centres agree
        Assert.NotEqual(b.Y, page.Groups[0].Bounds!.Y, 3); // but neither matches the container's centre
    }
}
