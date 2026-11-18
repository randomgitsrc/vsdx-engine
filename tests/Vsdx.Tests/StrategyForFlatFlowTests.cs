using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A container-less flow should be laid out along its edges, not as bands.
///
/// `workflow` only chose Flow when a decision diamond was present; otherwise Layered. A pure data
/// flow has no decisions, so it was layered — and a chain of eight nodes is eight layers, measured
/// at 23.3" x 7.5", a 3.1:1 strip. The flow engine produced 8.5" x 15.6" and scored higher (98 vs
/// 96), so there is no trade-off to weigh: the strip was not buying anything.
///
/// Layering stays right whenever containers exist, because a band is a thing the author drew and
/// the layout has to respect it.
/// </summary>
public sealed class StrategyForFlatFlowTests
{
    private static Diagram FlatFlow(int nodes = 6, bool withDecision = false)
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 9 } };

        for (int i = 0; i < nodes; i++)
        {
            page.Nodes.Add(new Node
            {
                Id = $"n{i}", Text = $"N{i}",
                Shape = withDecision && i == nodes / 2 ? "Diamond" : "Rectangle",
                Bounds = new Bounds(0, 0, 1.4, 0.7)
            });

            if (i > 0) page.Edges.Add(new Edge { Id = $"e{i}", From = $"n{i - 1}", To = $"n{i}" });
        }

        return new Diagram { Id = "t", Type = "workflow", Pages = { page } };
    }

    [Fact]
    public void AContainerlessWorkflowPicksFlow()
    {
        Assert.Equal(LayoutStrategy.Flow, DiagramLayoutEngine.SuggestStrategy(FlatFlow()));
    }

    [Fact]
    public void AWorkflowWithDecisionsStillPicksFlow()
    {
        Assert.Equal(LayoutStrategy.Flow, DiagramLayoutEngine.SuggestStrategy(FlatFlow(withDecision: true)));
    }

    [Fact]
    public void AContainedWorkflowStillPicksLayered()
    {
        // Bands are something the author drew; a chain inside them must not override that.
        var diagram = FlatFlow();
        diagram.Pages[0].Groups.Add(new Group
        {
            Id = "band", Bounds = new Bounds(6, 4, 8, 2), Members = { "n0", "n1" }
        });

        Assert.Equal(LayoutStrategy.Layered, DiagramLayoutEngine.SuggestStrategy(diagram));
    }

    [Fact]
    public void AnArchitectureWithContainersStillPicksLayered()
    {
        var diagram = FlatFlow();
        diagram.Type = "architecture";
        diagram.Pages[0].Groups.Add(new Group { Id = "g", Bounds = new Bounds(6, 4, 8, 2), Members = { "n0" } });

        Assert.Equal(LayoutStrategy.Layered, DiagramLayoutEngine.SuggestStrategy(diagram));
    }

    [Fact]
    public void AShortChainWithoutContainersPicksFlow()
    {
        // Short or long, the reasoning is the same; a two-node chain has one edge to follow.
        Assert.Equal(LayoutStrategy.Flow, DiagramLayoutEngine.SuggestStrategy(FlatFlow(nodes: 2)));
    }
}
