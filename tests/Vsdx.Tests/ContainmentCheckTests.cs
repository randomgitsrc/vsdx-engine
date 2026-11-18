using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The containment check must look at child groups, not only at member nodes.
///
/// It iterated `group.Members` and compared each node's box with the container — `ParentId` was
/// never read. A nested group could therefore sit half outside its parent with
/// `CheckContainment` returning nothing, so the one guarantee C1 just established had no check
/// guarding it.
///
/// Property-based: whatever the engine produces must satisfy this, and a box moved outside must be
/// caught.
/// </summary>
public sealed class ContainmentCheckTests
{
    private static Diagram Nested(out Group parent, out Group child)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 10 }
        };

        // Geometry has to agree with membership, or the "accepted" case fails for a reason
        // unrelated to what it tests: inner spans x 3.5-6.5, so b at x=7 was outside its own group.
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(3.5, 5, 1.4, 0.7) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(6.0, 5, 1.4, 0.7) });

        parent = new Group { Id = "outer", Bounds = new Bounds(5, 5, 8, 4) };
        child = new Group { Id = "inner", ParentId = "outer", Bounds = new Bounds(6.0, 5, 3, 2) };
        parent.Members.Add("a");
        child.Members.Add("b");

        page.Groups.Add(parent);
        page.Groups.Add(child);

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void AChildInsideItsParentIsAccepted()
    {
        var diagram = Nested(out _, out _);

        Assert.Empty(DiagramLayout.CheckContainment(diagram.Pages[0]));
    }

    [Fact]
    public void AChildEscapingItsParentIsReported()
    {
        // Everything a member check would look at is still fine: node b stays inside inner. Only the
        // child-to-parent relationship is broken.
        var diagram = Nested(out var parent, out var child);
        child.Bounds = new Bounds(5, 12, 3, 2);

        var problems = DiagramLayout.CheckContainment(diagram.Pages[0]);

        Assert.Contains(problems, p => p.Contains("inner") && p.Contains("outer"));
    }

    [Fact]
    public void AChildPartlyOutsideIsReported()
    {
        var diagram = Nested(out _, out var child);
        // Right edge at 10.5 against a parent reaching 9.0.
        child.Bounds = new Bounds(9, 5, 3, 2);

        Assert.NotEmpty(DiagramLayout.CheckContainment(diagram.Pages[0]));
    }

    [Fact]
    public void TheMessageNamesBothGroups()
    {
        var diagram = Nested(out _, out var child);
        child.Bounds = new Bounds(5, 12, 3, 2);

        var problem = DiagramLayout.CheckContainment(diagram.Pages[0]).First();

        Assert.Contains("'inner'", problem);
        Assert.Contains("'outer'", problem);
    }

    [Fact]
    public void ThreeLevelsAreEachChecked()
    {
        var diagram = Nested(out var parent, out var child);

        var deep = new Group { Id = "deep", ParentId = "inner", Bounds = new Bounds(5, 5, 1.5, 1) };
        deep.Members.Add("b");
        diagram.Pages[0].Groups.Add(deep);

        // Move only the middle one out; both its own relation and its child's become invalid.
        child.Bounds = new Bounds(5, 12, 3, 2);

        var problems = DiagramLayout.CheckContainment(diagram.Pages[0]);

        Assert.Contains(problems, p => p.Contains("'inner'") && p.Contains("'outer'"));
        Assert.Contains(problems, p => p.Contains("'deep'") && p.Contains("'inner'"));
    }
}
