using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// LocalRelayoutTests: re-laying out one container must not disturb anything outside it.
///
/// Phase 7 gap G5 found the repair loop could only do whole-page relayout, which moves unrelated
/// objects — the opposite of brief §6.9's "protect unrelated objects".
/// </summary>
public sealed class LocalRelayoutTests
{
    private static Diagram TwoBands()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 12 },
            Groups =
            {
                new Group { Id = "top", Name = "Top", Members = { "a", "b", "c" } },
                new Group { Id = "bottom", Name = "Bottom", Members = { "x", "y" } }
            },
            Nodes =
            {
                // Deliberately messy inside "top" so relayout has something to fix.
                new Node { Id = "a", Text = "A", Bounds = new Bounds(9.0, 9.0, 2, 0.9), GroupId = "top" },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(3.0, 8.6, 2, 0.9), GroupId = "top" },
                new Node { Id = "c", Text = "C", Bounds = new Bounds(6.0, 9.4, 2, 0.9), GroupId = "top" },
                new Node { Id = "x", Text = "X", Bounds = new Bounds(4.0, 3.0, 2, 0.9), GroupId = "bottom" },
                new Node { Id = "y", Text = "Y", Bounds = new Bounds(8.0, 3.0, 2, 0.9), GroupId = "bottom" }
            },
            Edges =
            {
                new Edge { Id = "ab", From = "a", To = "b" },
                new Edge { Id = "bc", From = "b", To = "c" },
                new Edge { Id = "xy", From = "x", To = "y" }
            }
        };

        return new Diagram { Type = "architecture", PageSize = page.Size, Pages = { page } };
    }

    [Fact]
    public void Local_MovesOnlyTargetContainerMembers()
    {
        var diagram = TwoBands();
        var before = diagram.Pages[0].Nodes
            .Where(n => n.GroupId != "top")
            .ToDictionary(n => n.Id, n => (n.Bounds.X, n.Bounds.Y, n.Bounds.W, n.Bounds.H));

        var result = LocalRelayout.Apply(diagram, "top", LayoutStrategy.Layered);

        Assert.True(result.Ok, result.Note);
        Assert.False(result.IsolationBroken);

        foreach (var (id, snapshot) in before)
        {
            var node = diagram.Pages[0].Nodes.Single(n => n.Id == id);
            Assert.Equal(snapshot, (node.Bounds.X, node.Bounds.Y, node.Bounds.W, node.Bounds.H));
        }
    }

    [Fact]
    public void Local_ReportsWhichNodesMoved()
    {
        var diagram = TwoBands();
        var result = LocalRelayout.Apply(diagram, "top", LayoutStrategy.Layered);

        Assert.Equal(3, result.Moved.Count);
        Assert.Contains("a", result.Moved);
        Assert.Contains("b", result.Moved);
        Assert.Contains("c", result.Moved);
    }

    [Fact]
    public void Local_OrdersMembersAlongEdges()
    {
        var diagram = TwoBands();
        LocalRelayout.Apply(diagram, "top", LayoutStrategy.Layered);

        var page = diagram.Pages[0];
        var a = page.Nodes.Single(n => n.Id == "a");
        var b = page.Nodes.Single(n => n.Id == "b");
        var c = page.Nodes.Single(n => n.Id == "c");

        // a -> b -> c must read left to right afterwards.
        Assert.True(a.Bounds.X < b.Bounds.X);
        Assert.True(b.Bounds.X < c.Bounds.X);
    }

    [Fact]
    public void Local_AlignsMembersToSingleRow()
    {
        var diagram = TwoBands();
        LocalRelayout.Apply(diagram, "top", LayoutStrategy.Layered);

        var rows = diagram.Pages[0].Nodes
            .Where(n => n.GroupId == "top")
            .Select(n => n.Bounds.Y)
            .Distinct()
            .Count();

        Assert.Equal(1, rows);
    }

    [Fact]
    public void Local_KeepsContainerAnchor()
    {
        var diagram = TwoBands();
        // Give the container an explicit position first.
        DiagramLayout.Apply(diagram);
        var beforeCentre = diagram.Pages[0].Groups.Single(g => g.Id == "top").Bounds!.X;

        LocalRelayout.Apply(diagram, "top", LayoutStrategy.Layered);

        var after = diagram.Pages[0].Groups.Single(g => g.Id == "top").Bounds!;
        Assert.Equal(beforeCentre, after.X, 3);
    }

    [Fact]
    public void Local_UnknownGroupIsReported()
    {
        var diagram = TwoBands();
        var result = LocalRelayout.Apply(diagram, "does-not-exist");

        Assert.False(result.Ok);
        Assert.Contains("找不到", result.Note!);
    }

    [Fact]
    public void Local_EmptyContainerIsReported()
    {
        var diagram = TwoBands();
        diagram.Pages[0].Groups.Add(new Group { Id = "empty", Name = "Empty" });

        var result = LocalRelayout.Apply(diagram, "empty");

        Assert.False(result.Ok);
        Assert.Contains("没有节点", result.Note!);
    }

    [Fact]
    public void Local_GridStrategyWorks()
    {
        var diagram = TwoBands();
        var result = LocalRelayout.Apply(diagram, "top", LayoutStrategy.Grid);

        Assert.True(result.Ok, result.Note);
        Assert.Equal(3, result.Moved.Count);
    }

    [Fact]
    public void Local_SurvivesCyclicEdges()
    {
        var diagram = TwoBands();
        diagram.Pages[0].Edges.Add(new Edge { Id = "ca", From = "c", To = "a" });

        var result = LocalRelayout.Apply(diagram, "top", LayoutStrategy.Layered);

        Assert.True(result.Ok, result.Note);
        Assert.Equal(3, result.Moved.Count);
    }
}
