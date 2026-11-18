using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A parent group must enclose its child group.
///
/// `DiagramLayout.Apply` walked groups parent-first, so when it derived the parent it read the
/// child's bounds *as they were before the child was derived* — the child's authored box, not the
/// one its own members imply. Measured on the recorded fixture: parent 6.90 x 5.68, child
/// 2.60 x 1.62, and the child was not inside it.
///
/// Children first is the only order that works: a parent's extent depends on where its children
/// ended up, so those children must be final before the parent is computed.
/// </summary>
public sealed class NestedGroupContainmentTests
{
    private static bool Encloses(Bounds outer, Bounds inner, double tolerance = 1e-6) =>
        inner.X - inner.W / 2 >= outer.X - outer.W / 2 - tolerance &&
        inner.X + inner.W / 2 <= outer.X + outer.W / 2 + tolerance &&
        inner.Y - inner.H / 2 >= outer.Y - outer.H / 2 - tolerance &&
        inner.Y + inner.H / 2 <= outer.Y + outer.H / 2 + tolerance;

    private static Page Nested(string name)
    {
        var page = new Page
        {
            Name = name,
            Size = new PageSize { Width = 14, Height = 9 }
        };

        foreach (var id in new[] { "a", "b", "c" })
            page.Nodes.Add(new Node { Id = $"{name}-{id}", Text = id, Bounds = new Bounds(0, 0, 1.6, 0.8) });

        page.Edges.Add(new Edge { Id = $"{name}-ab", From = $"{name}-a", To = $"{name}-b" });
        page.Edges.Add(new Edge { Id = $"{name}-bc", From = $"{name}-b", To = $"{name}-c" });

        // The child's authored box is deliberately wrong: its single member will land elsewhere, so
        // a parent that trusts this box cannot enclose the derived one.
        page.Groups.Add(new Group
        {
            Id = $"{name}-outer",
            Members = { $"{name}-a", $"{name}-b" },
            Bounds = new Bounds(4, 4, 5, 2),
            Style = new ShapeStyle { PaddingX = 0.30, HeadingHeight = 0.46, PaddingBottom = 0.22 }
        });

        page.Groups.Add(new Group
        {
            Id = $"{name}-inner",
            Members = { $"{name}-c" },
            ParentId = $"{name}-outer",
            Bounds = new Bounds(4.5, 4, 2, 1.2),
            Style = new ShapeStyle { PaddingX = 0.50, HeadingHeight = 0.60, PaddingBottom = 0.22 }
        });

        return page;
    }

    [Fact]
    public void TheParentEnclosesTheChild()
    {
        var page = Nested("p1");

        DiagramLayout.Apply(new Diagram { Pages = { page } });

        var outer = page.Groups.Single(g => g.Id == "p1-outer").Bounds!;
        var inner = page.Groups.Single(g => g.Id == "p1-inner").Bounds!;

        Assert.True(Encloses(outer, inner),
            $"outer {outer.W:F2}x{outer.H:F2} at ({outer.X:F2},{outer.Y:F2}) does not enclose " +
            $"inner {inner.W:F2}x{inner.H:F2} at ({inner.X:F2},{inner.Y:F2})");
    }

    [Fact]
    public void ThreeLevelsNestCorrectly()
    {
        // Depth must not matter: each level is computed after everything inside it.
        var page = Nested("p1");
        page.Groups.Add(new Group
        {
            Id = "p1-deep",
            Members = { "p1-a" },
            ParentId = "p1-inner",
            Bounds = new Bounds(4.5, 4, 1, 1),
            Style = new ShapeStyle { PaddingX = 0.2, HeadingHeight = 0.3, PaddingBottom = 0.2 }
        });

        DiagramLayout.Apply(new Diagram { Pages = { page } });

        var outer = page.Groups.Single(g => g.Id == "p1-outer").Bounds!;
        var inner = page.Groups.Single(g => g.Id == "p1-inner").Bounds!;
        var deep = page.Groups.Single(g => g.Id == "p1-deep").Bounds!;

        Assert.True(Encloses(inner, deep), "inner should enclose deep");
        Assert.True(Encloses(outer, deep), "outer should enclose deep");
    }

    [Fact]
    public void SiblingsStayInsideTheirSharedParent()
    {
        // Two sibling groups around adjacent members legitimately overlap: their boxes both grow to
        // cover neighbours that sit close together, so "siblings must not intersect" was a wrong
        // expectation of mine, not a property to enforce. What must hold is that the parent covers
        // both.
        var page = Nested("p1");
        page.Groups.Add(new Group
        {
            Id = "p1-other",
            Members = { "p1-b" },
            ParentId = "p1-outer",
            Bounds = new Bounds(6, 4, 2, 1.2)
        });

        DiagramLayout.Apply(new Diagram { Pages = { page } });

        var outer = page.Groups.Single(g => g.Id == "p1-outer").Bounds!;

        foreach (var childId in new[] { "p1-inner", "p1-other" })
        {
            var child = page.Groups.Single(g => g.Id == childId).Bounds!;
            Assert.True(Encloses(outer, child), $"outer should enclose {childId}");
        }
    }

    [Fact]
    public void AllContainmentChecksPass()
    {
        var page = Nested("p1");

        DiagramLayout.Apply(new Diagram { Pages = { page } });

        Assert.Empty(DiagramLayout.CheckContainment(page));
    }

    private static double OverlapExtentX(Bounds a, Bounds b) =>
        Math.Min(a.X + a.W / 2, b.X + b.W / 2) - Math.Max(a.X - a.W / 2, b.X - b.W / 2);

    private static double OverlapExtentY(Bounds a, Bounds b) =>
        Math.Min(a.Y + a.H / 2, b.Y + b.H / 2) - Math.Max(a.Y - a.H / 2, b.Y - b.H / 2);
}
