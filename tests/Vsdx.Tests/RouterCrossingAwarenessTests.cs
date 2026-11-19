using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The router routes each edge once and never looks at the ones already placed, so it cannot prefer the side
/// combination that produces fewer crossings — it cannot see crossings at all.
///
/// `RoutePage` iterates `page.Edges` and calls `RouteBetween` for each. It does search side combinations, but only
/// when the preferred one is `Blocked`, and "blocked" means the route cuts through a shape. A route that crosses
/// three other connectors is perfectly acceptable to it.
///
/// That is a design gap rather than a weak heuristic: the search this would need already exists, it is just
/// scoring on one criterion. Adding crossings to that score is a small change; changing the iteration to consider
/// already-routed edges is the real work.
/// </summary>
public sealed class RouterCrossingAwarenessTests
{
    private static Diagram TwoInBandEdgesAndOneSpanning()
    {
        // Modelled on the crossing that survives in the agent's diagram: an edge inside a band, crossed by an
        // edge leaving that band.
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 8 } };

        page.Groups.Add(new Group { Id = "top", Name = "上", Kind = "layer", Order = 2, Members = { "a", "b" } });
        page.Groups.Add(new Group { Id = "bot", Name = "下", Kind = "layer", Order = 1, Members = { "c", "d" } });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "top", Bounds = new Bounds(2, 6, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "top", Bounds = new Bounds(8, 6, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "c", Text = "C", GroupId = "bot", Bounds = new Bounds(2, 2, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "d", Text = "D", GroupId = "bot", Bounds = new Bounds(8, 2, 1.4, 0.6) });

        page.Edges.Add(new Edge { Id = "inband", From = "a", To = "b" });
        page.Edges.Add(new Edge { Id = "span", From = "b", To = "c" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void StraightEdgesThatCutNothingAreLeftAlone()
    {
        // My first version of this test expected both edges to be re-routed. They are not, and that is correct:
        // a straight edge is only given an orthogonal detour when it would cut through another *shape*. What the
        // router never considers is whether the straight line crosses another *connector*.
        var diagram = TwoInBandEdgesAndOneSpanning();

        OrthogonalRouter.Route(diagram);

        Assert.All(diagram.Pages[0].Edges, e => Assert.NotEqual("rightAngle", e.Kind));
        Assert.All(diagram.Pages[0].Edges, e => Assert.True(e.Waypoints is null or { Count: 0 }));
    }

    [Fact]
    public void RoutingIsOrderIndependentForThisShape()
    {
        // Whatever crossings result, they must not depend on the order edges happen to be declared in —
        // otherwise the output changes when an unrelated edge is added.
        static int CountAfterRouting(bool reverse)
        {
            var diagram = TwoInBandEdgesAndOneSpanning();

            if (reverse)
            {
                var edges = diagram.Pages[0].Edges;
                edges.Reverse();
            }

            OrthogonalRouter.Route(diagram);

            return LayoutAnalyzer.Analyze(diagram).Issues.Count(i => i.Code == "edge.crossing");
        }

        Assert.Equal(CountAfterRouting(false), CountAfterRouting(true));
    }

    [Fact]
    public void ASideCombinationSearchExistsAndIsUsedOnlyWhenBlocked()
    {
        // The search that a crossing-aware router would extend. Proving it exists keeps the claim honest: the
        // work is in the scoring, not in inventing the mechanism.
        var source = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Engine", "OrthogonalRouter.cs"));

        Assert.Contains("SidePairs", source);
        Assert.Contains("attempt.Blocked", source);
        Assert.DoesNotContain("crossing", source, StringComparison.OrdinalIgnoreCase);
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
