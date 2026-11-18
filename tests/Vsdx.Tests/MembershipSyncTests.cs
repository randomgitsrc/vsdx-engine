using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `Group.Members` and `Node.GroupId` must agree before layout runs.
///
/// The model says Members is "kept in sync with Node.GroupId", but nothing performs that sync. The
/// two carriers therefore diverge depending on where a diagram came from:
///
///   benchmark JSON   containers carry no members; nodes carry groupId
///   execute path     the IR converter fills members from containerId, and leaves groupId too
///
/// Every layout engine reads `Members`, so a diagram whose membership lives only in `group_id`
/// silently looks like it has no containers. Measured on benchmarks/workflow/medium: members
/// 0,0,0 with three populated groupIds, which is why the flow engine never saw those bands — and
/// why the spec path, which does fill members, took a code path no benchmark exercises.
///
/// Normalisation is the fix: one direction, applied before layout, so every engine sees the same
/// membership whatever produced the model.
/// </summary>
public sealed class MembershipSyncTests
{
    private static Diagram FromGroupIdsOnly()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 9 } };

        page.Groups.Add(new Group { Id = "band", Name = "Band" });
        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "band", Bounds = new Bounds(3, 4, 1.4, 0.7) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "band", Bounds = new Bounds(6, 4, 1.4, 0.7) });
        page.Nodes.Add(new Node { Id = "c", Text = "C", Bounds = new Bounds(9, 4, 1.4, 0.7) });

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void GroupIdPopulatesMembers()
    {
        var diagram = FromGroupIdsOnly();

        DiagramLayoutEngine.NormaliseMembership(diagram);

        var band = diagram.Pages[0].Groups[0];
        Assert.Equal(new[] { "a", "b" }, band.Members.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void NodesOutsideAnyContainerStayOut()
    {
        var diagram = FromGroupIdsOnly();

        DiagramLayoutEngine.NormaliseMembership(diagram);

        Assert.DoesNotContain("c", diagram.Pages[0].Groups[0].Members);
    }

    [Fact]
    public void ExistingMembersAreNotDuplicated()
    {
        // The execute path already fills both, so normalising must be idempotent or the second run
        // would double every entry.
        var diagram = FromGroupIdsOnly();
        diagram.Pages[0].Groups[0].Members.Add("a");
        diagram.Pages[0].Groups[0].Members.Add("b");

        DiagramLayoutEngine.NormaliseMembership(diagram);
        DiagramLayoutEngine.NormaliseMembership(diagram);

        Assert.Equal(2, diagram.Pages[0].Groups[0].Members.Count);
    }

    [Fact]
    public void AContainedDiagramIsLaidOutAsBandsAfterNormalising()
    {
        // The practical consequence: before normalising, this diagram looks container-less and the
        // flow engine spreads its nodes along one line.
        var diagram = FromGroupIdsOnly();
        DiagramLayoutEngine.NormaliseMembership(diagram);

        var strategy = DiagramLayoutEngine.SuggestStrategy(diagram);

        Assert.Equal(LayoutStrategy.Layered, strategy);
    }

    [Fact]
    public void ADiagramWithoutGroupIdsIsUnchanged()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 8 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 4, 1.4, 0.7) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(6, 4, 1.4, 0.7) }
            },
            Edges = { new Edge { Id = "e", From = "a", To = "b" } }
        };
        var diagram = new Diagram { Id = "t", Pages = { page } };

        DiagramLayoutEngine.NormaliseMembership(diagram);

        Assert.Empty(diagram.Pages[0].Groups);
    }
}
