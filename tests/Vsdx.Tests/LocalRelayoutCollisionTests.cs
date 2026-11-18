using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Relaying one container must not push it into another container's nodes.
///
/// `LocalRelayout` verified that nodes *outside* the target did not move — the isolation guarantee —
/// and reported success on that basis. It never checked whether the target's new layout landed on
/// top of them. Measured on overview.ir.json with `containerLayouts: {"back": "grid"}`: `web`
/// overlapped `api` by 1.71", `mobile` overlapped `worker` by 1.15", and `appliedDesignNotes` still
/// said "container back relaid as grid".
///
/// Isolation and non-collision are different promises: the first says "I left your other shapes
/// alone", the second says "the result is usable". Reporting the first as though it were the second
/// is what this pins.
/// </summary>
public sealed class LocalRelayoutCollisionTests
{
    private static Diagram TwoBands()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 10 }
        };

        page.Groups.Add(new Group { Id = "front", Bounds = new Bounds(7, 6, 10, 3) });
        page.Groups.Add(new Group { Id = "back", Bounds = new Bounds(7, 3, 10, 3) });

        page.Nodes.Add(new Node { Id = "f1", Text = "F1", GroupId = "front", Bounds = new Bounds(3, 6, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "f2", Text = "F2", GroupId = "front", Bounds = new Bounds(3, 5.2, 1.3, 0.55) });

        page.Nodes.Add(new Node { Id = "b1", Text = "B1", GroupId = "back", Bounds = new Bounds(8, 3, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "b2", Text = "B2", GroupId = "back", Bounds = new Bounds(9, 3, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "b3", Text = "B3", GroupId = "back", Bounds = new Bounds(10, 3, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "b4", Text = "B4", GroupId = "back", Bounds = new Bounds(11, 3, 1.3, 0.55) });

        return new Diagram { Id = "t", Pages = { page } };
    }

    private static bool Overlaps(Bounds a, Bounds b)
    {
        double ox = Math.Min(a.X + a.W / 2, b.X + b.W / 2) - Math.Max(a.X - a.W / 2, b.X - b.W / 2);
        double oy = Math.Min(a.Y + a.H / 2, b.Y + b.H / 2) - Math.Max(a.Y - a.H / 2, b.Y - b.H / 2);

        return ox > 0.05 && oy > 0.05;
    }

    [Fact]
    public void ACleanRelayoutReportsNoCollisions()
    {
        var diagram = TwoBands();

        var result = LocalRelayout.Apply(diagram, "back", LayoutStrategy.Grid);

        Assert.True(result.Ok, result.Note);
        Assert.Empty(result.Collisions);
    }

    [Fact]
    public void ANodeForcedOntoAnotherIsReported()
    {
        // Pin a front node where the grid actually places the back members. Measured: the grid puts
        // them at y=4.00/5.00 and x=9.00/10.85, so a node at (9, 3.2) misses them entirely — my
        // first fixture assumed a y the layout never uses and the test asserted nothing.
        var diagram = TwoBands();
        var page = diagram.Pages[0];
        page.Nodes.First(n => n.Id == "f1").Bounds = new Bounds(9, 5.0, 1.3, 0.55);

        var result = LocalRelayout.Apply(diagram, "back", LayoutStrategy.Grid);

        Assert.NotEmpty(result.Collisions);
        Assert.False(result.Ok, "a colliding relayout must not report Ok");
    }

    [Fact]
    public void TheCollisionNamesBothNodes()
    {
        var diagram = TwoBands();
        diagram.Pages[0].Nodes.First(n => n.Id == "f1").Bounds = new Bounds(9, 5.0, 1.3, 0.55);

        var result = LocalRelayout.Apply(diagram, "back", LayoutStrategy.Grid);

        var collision = result.Collisions.First();
        Assert.Contains("f1", collision);
        Assert.Contains("b", collision);
    }

    [Fact]
    public void IsolationStillHolds()
    {
        // The guarantee that already held must keep holding, collisions or not.
        var diagram = TwoBands();
        var page = diagram.Pages[0];
        var before = page.Nodes.Where(n => n.GroupId == "front")
            .ToDictionary(n => n.Id, n => (n.Bounds.X, n.Bounds.Y), StringComparer.Ordinal);

        LocalRelayout.Apply(page.Groups.Count > 0 ? diagram : diagram, "back", LayoutStrategy.Grid);

        foreach (var (id, pos) in before)
        {
            var node = page.Nodes.First(n => n.Id == id);
            Assert.Equal(pos.X, node.Bounds.X, 6);
            Assert.Equal(pos.Y, node.Bounds.Y, 6);
        }
    }

    [Fact]
    public void ARealOverlapIsDetectedByTheHelper()
    {
        // Guard against the overlap test itself being too lenient.
        Assert.True(Overlaps(new Bounds(0, 0, 2, 2), new Bounds(1, 1, 2, 2)));
        Assert.False(Overlaps(new Bounds(0, 0, 1, 1), new Bounds(5, 5, 1, 1)));
    }
}
