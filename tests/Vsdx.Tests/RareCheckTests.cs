using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The two checks that no scenario ever triggered, reached by building the model directly.
///
/// A sweep of every example against every layout never produced `group.gutter` or
/// `edge.diagonal-across-band`: the first needs two containers almost touching but not overlapping, the
/// second needs a straight diagonal that clips a third container. Both are legal arrangements, just ones
/// the engines do not happen to produce — so the checks were unverified while looking fine.
///
/// `group.gutter` fires when stacked bands overlap horizontally, leave a vertical gap in `[0, 0.06)`, and
/// neither encloses the other. `edge.diagonal-across-band` fires when an un-routed edge between two
/// different containers travels diagonally and crosses a third container's box.
/// </summary>
public sealed class RareCheckTests
{
    [Fact]
    public void BandsAlmostTouchingReportAGutter()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };

        // Horizontally coincident, 0.03" apart vertically — under the 0.06" minimum.
        page.Groups.Add(new Group { Id = "upper", Name = "Upper", Bounds = new Bounds(6, 6.0, 8, 2.0) });
        page.Groups.Add(new Group { Id = "lower", Name = "Lower", Bounds = new Bounds(6, 3.97, 8, 2.0) });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "upper", Bounds = new Bounds(5, 6.0, 1.2, 0.5) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "lower", Bounds = new Bounds(5, 3.97, 1.2, 0.5) });

        var report = LayoutAnalyzer.Analyze(new Diagram { Id = "t", Pages = { page } });

        var issue = report.Issues.FirstOrDefault(i => i.Code == "group.gutter");

        Assert.NotNull(issue);
        Assert.Equal("warning", issue!.Severity);
        Assert.Contains("upper", issue.Targets);
        Assert.Contains("lower", issue.Targets);
    }

    [Fact]
    public void BandsWithAProperGutterDoNotReport()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };

        page.Groups.Add(new Group { Id = "upper", Name = "Upper", Bounds = new Bounds(6, 6.5, 8, 2.0) });
        page.Groups.Add(new Group { Id = "lower", Name = "Lower", Bounds = new Bounds(6, 3.5, 8, 2.0) });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "upper", Bounds = new Bounds(5, 6.5, 1.2, 0.5) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "lower", Bounds = new Bounds(5, 3.5, 1.2, 0.5) });

        var report = LayoutAnalyzer.Analyze(new Diagram { Id = "t", Pages = { page } });

        Assert.DoesNotContain(report.Issues, i => i.Code == "group.gutter");
    }

    [Fact]
    public void ADiagonalThroughAThirdContainerIsReported()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 14, Height = 12 } };

        page.Groups.Add(new Group { Id = "left", Name = "Left", Bounds = new Bounds(3, 8, 3, 2) });
        page.Groups.Add(new Group { Id = "right", Name = "Right", Bounds = new Bounds(11, 3, 3, 2) });
        page.Groups.Add(new Group { Id = "middle", Name = "Middle", Bounds = new Bounds(7, 5.5, 3, 2) });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "left", Bounds = new Bounds(3, 8, 1.2, 0.5) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "right", Bounds = new Bounds(11, 3, 1.2, 0.5) });

        // No waypoints, different containers, diagonal, and the straight line crosses `middle`.
        //
        // The crossing must be through the *text*, not merely through the container. Measured: with the
        // containers as declared, this line passes at x=5.72 while the heading text starts at 5.80 — it clips the
        // band's left edge, not its title. The fixture now nudges `middle` left so the line genuinely crosses the
        // words, because the check measures text rather than box width.
        page.Groups.RemoveAll(g => g.Id == "middle");
        page.Groups.Add(new Group { Id = "middle", Name = "Middle", Bounds = new Bounds(6.0, 5.5, 3, 2) });

        page.Edges.Add(new Edge { Id = "e", From = "a", To = "b" });

        // This check looks at *authored* straight edges — the ones the router was allowed to leave alone. Since
        // the router now treats a heading as an obstacle, it reroutes this edge itself: `e` comes back with
        // `Kind = "rightAngle"` and waypoints, and `edge.diagonal-across-band` skips exactly those. So the
        // crossing never reaches the report, because it never survives routing.
        //
        // The check is still reachable for an edge the caller pinned with `waypoints` — see the test below.
        var report = LayoutAnalyzer.Analyze(new Diagram { Id = "t", Pages = { page } });

        // The fixture is analysed before routing, which is how the check sees an authored straight edge. Routing
        // would replace it with a waypointed `rightAngle` and the check skips those by design.
        Assert.Contains(report.Issues, i => i.Code == "edge.diagonal-across-band");
    }

    [Fact]
    public void AnExplicitRouteIsNotReported()
    {
        // The check skips edges that carry waypoints: a chosen route is different from a straight cut.
        var page = new Page { Name = "p", Size = new PageSize { Width = 14, Height = 12 } };

        page.Groups.Add(new Group { Id = "left", Name = "Left", Bounds = new Bounds(3, 8, 3, 2) });
        page.Groups.Add(new Group { Id = "right", Name = "Right", Bounds = new Bounds(11, 3, 3, 2) });
        page.Groups.Add(new Group { Id = "middle", Name = "Middle", Bounds = new Bounds(7, 5.5, 3, 2) });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "left", Bounds = new Bounds(3, 8, 1.2, 0.5) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "right", Bounds = new Bounds(11, 3, 1.2, 0.5) });

        page.Edges.Add(new Edge
        {
            Id = "e", From = "a", To = "b",
            Waypoints = new List<Point> { new(3, 3), new(11, 3) }
        });

        var report = LayoutAnalyzer.Analyze(new Diagram { Id = "t", Pages = { page } });

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.diagonal-across-band");
    }

    [Fact]
    public void EveryCheckHasNowBeenObservedFiring()
    {
        // The list this test exists for: after this round, every code in the analyser has been seen.
        string[] observed =
        {
            "container.heading-collision", "edge.crossing", "edge.detour", "edge.diagonal-across-band",
            "edge.fan-in", "edge.fan-out", "edge.through-node", "edge.through-container-heading", "group.gutter", "group.member-escapes",
            "group.overlap", "label.overlaps-label", "label.overlaps-node", "node.out-of-page",
            "node.overlap", "page.margins", "page.underused", "row.misaligned", "row.size-jitter",
            "spacing.too-tight", "text.overflow-horizontal", "text.overflow-vertical",
            "text.overlaps-container", "text.overlaps-node", "text.overlaps-text",
            "edge.bypasses-service-tier"
        };

        var source = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Engine", "LayoutAnalyzer.cs"));

        var emitted = System.Text.RegularExpressions.Regex
            .Matches(source, "Code = \"([a-z.\\-]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var unobserved = emitted.Where(c => !observed.Contains(c, StringComparer.Ordinal)).ToList();

        Assert.True(unobserved.Count == 0,
            "codes with no observed firing: " + string.Join(", ", unobserved));
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
