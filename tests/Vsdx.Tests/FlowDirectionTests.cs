using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A top-to-bottom flow must start at the top.
///
/// The engine placed rows by a cursor starting at the page margin and increasing, so the first row
/// in the reading order landed at the *bottom*: measured on the data-flow diagram with the flow
/// engine, `web` sat at y=1.48 and `enrich` at 13.82 while VSDX grows upward, so the whole chain
/// read bottom-up. The analyser scored it 98 — nothing checks which way a diagram flows.
///
/// Mirrors C17, which fixed the same symptom after a transpose. Both came from "row N sits at
/// margin + N x step" while the axis grows in the other direction.
/// </summary>
public sealed class FlowDirectionTests
{
    private static Diagram Chain(int count)
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 9 } };

        for (int i = 0; i < count; i++)
        {
            page.Nodes.Add(new Node { Id = $"n{i}", Text = $"N{i}", Bounds = new Bounds(0, 0, 1.4, 0.7) });
            if (i > 0) page.Edges.Add(new Edge { Id = $"e{i}", From = $"n{i - 1}", To = $"n{i}" });
        }

        return new Diagram { Id = "t", Type = "workflow", Pages = { page } };
    }

    [Fact]
    public void TheFirstNodeInTheChainSitsAboveTheLast()
    {
        var diagram = Chain(5);

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Flow);

        var byId = diagram.Pages[0].Nodes.ToDictionary(n => n.Id, n => n.Bounds.Y, StringComparer.Ordinal);

        Assert.True(byId["n0"] > byId["n4"],
            $"n0 at y={byId["n0"]:F2} should be above n4 at y={byId["n4"]:F2}");
    }

    [Fact]
    public void TheChainStepsDownWithinEachColumn()
    {
        // Flow runs its sequence left to right and wraps when it runs out of width, so a long chain
        // becomes several columns. Each column descends — that is the part "top to bottom" governs;
        // requiring the whole chain to be monotonic in Y would demand a single column of unbounded
        // height.
        var diagram = Chain(6);

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Flow);

        var nodes = diagram.Pages[0].Nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToList();

        foreach (var column in nodes.GroupBy(n => Math.Round(n.Bounds.X, 1)))
        {
            var ys = column.Select(n => n.Bounds.Y).ToList();
            for (int i = 1; i < ys.Count; i++)
            {
                Assert.True(ys[i - 1] > ys[i],
                    $"column x={column.Key:F1}: {ys[i - 1]:F2} is not above {ys[i]:F2}");
            }
        }
    }

    [Fact]
    public void EverythingStaysOnThePage()
    {
        var diagram = Chain(5);

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Flow);

        var page = diagram.Pages[0];
        foreach (var node in page.Nodes)
        {
            Assert.InRange(node.Bounds.Y - node.Bounds.H / 2, 0, page.Size!.Height);
            Assert.InRange(node.Bounds.Y + node.Bounds.H / 2, 0, page.Size.Height);
        }
    }

    [Fact]
    public void LeftToRightIsUnaffected()
    {
        // The fix is about the vertical axis; x ordering must not move.
        var diagram = Chain(5);

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Direction = LayoutDirection.LeftToRight },
            LayoutStrategy.Flow);

        var byId = diagram.Pages[0].Nodes.ToDictionary(n => n.Id, n => n.Bounds.X, StringComparer.Ordinal);

        Assert.True(byId["n0"] < byId["n4"],
            $"n0 at x={byId["n0"]:F2} should be left of n4 at x={byId["n4"]:F2}");
    }
}
