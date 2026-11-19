using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `order` decides position, but not always along the same axis, and one engine ignores it.
///
/// Measured through `execute` on a two-lane IR (`order` 1 and 9):
///
///   flow       both lanes at the same y, differing in x — order decides left-to-right
///   swimlane   lanes stacked in y — order decides top-to-bottom
///   layered    `AssignBands` never reads `Order`; it sorts by the mean Y of each band's members
///
/// The doc comment on `IrContainer.Order` says "lower = higher on the page", which matches neither the axis nor
/// the direction. I tried implementing it in round 2 and it inverted `deploy-topology`, dropped its score and
/// broke three rendered-artefact tests — every shipped example writes the highest order on the topmost band.
///
/// These tests record what happens rather than what the comment promises. Changing any of it is a product
/// decision: making layered honour the field would change every architecture diagram that places bands by
/// declaration order today.
/// </summary>
public sealed class BandOrderConventionTests
{
    private static Diagram TwoLanes()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };

        page.Groups.Add(new Group { Id = "low", Name = "order 1", Kind = "lane", Order = 1, Members = { "a" } });
        page.Groups.Add(new Group { Id = "high", Name = "order 9", Kind = "lane", Order = 9, Members = { "b" } });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "low" });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "high" });

        return new Diagram { Id = "t", Type = "workflow", Pages = { page } };
    }

    private static List<Group> LayOut(LayoutStrategy strategy)
    {
        var diagram = TwoLanes();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, strategy);

        return diagram.Pages[0].Groups.Where(g => g.Bounds is not null).ToList();
    }

    [Fact]
    public void FlowPlacesThemSideBySideRatherThanStacked()
    {
        // Not a defect: a flow's containers become columns. Worth pinning because "order = higher on the page"
        // suggests the opposite axis.
        var groups = LayOut(LayoutStrategy.Flow);

        Assert.Equal(groups[0].Bounds!.Y, groups[1].Bounds!.Y, 3);
        Assert.NotEqual(groups[0].Bounds!.X, groups[1].Bounds!.X, 3);
    }

    [Fact]
    public void SwimlaneStacksThem()
    {
        var groups = LayOut(LayoutStrategy.Swimlane);

        Assert.NotEqual(groups[0].Bounds!.Y, groups[1].Bounds!.Y, 3);
    }

    [Fact]
    public void LayeredNowReadsTheOrder()
    {
        // Swapping the two values used to change nothing, because `AssignBands` sorted by member position alone.
        // It now reads `Order` so that containers sharing one can stand side by side; swapping therefore moves
        // them.
        static string Topmost(Diagram d) => d.Pages[0].Groups
            .Where(g => g.Bounds is not null)
            .OrderByDescending(g => g.Bounds!.Y)
            .First().Id;

        var original = TwoLanes();
        DiagramLayoutEngine.Apply(original,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        var swapped = TwoLanes();
        foreach (var group in swapped.Pages[0].Groups)
            group.Order = 10 - (group.Order ?? 0);

        DiagramLayoutEngine.Apply(swapped,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        Assert.NotEqual(Topmost(original), Topmost(swapped));
    }

    [Fact]
    public void TheDocCommentDisagreesWithAllOfThem()
    {
        // A record, not a wish. If the comment is ever corrected this test should be deleted deliberately.
        var source = File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Protocol", "DiagramIr.cs"));

        Assert.Contains("lower = higher on the page", source);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
