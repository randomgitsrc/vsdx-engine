using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Vertical routing between nodes that share a column.
///
/// The router builds vertical channels from the gaps *between* node columns. Two nodes stacked
/// in the same column have no gap between them, so the search fell through to the outermost
/// channel on the page — measured at x=1.10 and x=15.30 on a 16-inch page for edges whose
/// endpoints were both at x=8.20. The result was a route crossing the whole diagram and passing
/// through unrelated shapes, on the layout the project's own skill document recommends
/// ("put each data store directly under the service that owns it").
///
/// These tests pin the direct-vertical case so it cannot regress.
/// </summary>
public sealed class RouterChannelTests
{
    private static Diagram StackedPair(double x, double topY, double bottomY, double pageWidth = 16.0)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = pageWidth, Height = 12 },
            Nodes =
            {
                new Node { Id = "top", Text = "Top", Bounds = new Bounds(x, topY, 2.1, 0.9) },
                new Node { Id = "bottom", Text = "Bottom", Bounds = new Bounds(x, bottomY, 2.1, 1.0) }
            },
            Edges = { new Edge { Id = "e", From = "top", To = "bottom", Kind = "rightAngle" } }
        };

        return new Diagram { Pages = { page } };
    }

    /// <summary>
    /// The shape that actually reproduced the defect: several columns of services above their
    /// stores, plus a wide element spanning the middle. A single isolated pair routed fine, which
    /// is why the two-node case above was not enough to catch it.
    /// </summary>
    private static Diagram ServiceStoreGrid()
    {
        double[] columns = { 2.60, 5.40, 8.20, 11.00, 13.80 };
        string[] services = { "order", "pay", "customs", "stock", "fx" };
        string[] stores = { "pg", "redis", "es", "s3" };

        var page = new Page { Name = "p", Size = new PageSize { Width = 16, Height = 12 } };

        for (int i = 0; i < services.Length; i++)
            page.Nodes.Add(new Node { Id = services[i], Text = services[i], Bounds = new Bounds(columns[i], 7.70, 2.10, 0.90) });

        for (int i = 0; i < stores.Length; i++)
            page.Nodes.Add(new Node { Id = stores[i], Text = stores[i], Bounds = new Bounds(columns[i], 2.50, 2.10, 1.00) });

        // Narrow enough to leave the outer columns clear. A full-width bus blocks every vertical
        // lane, in which case detouring around it is the correct answer rather than a defect.
        page.Nodes.Add(new Node { Id = "bus", Text = "bus", Shape = "Parallelogram", Bounds = new Bounds(8.20, 5.20, 3.60, 0.70) });

        foreach (var (from, to) in new[] { ("order", "pg"), ("pay", "redis"), ("customs", "es"), ("stock", "s3") })
            page.Edges.Add(new Edge { Id = $"{from}-{to}", From = from, To = to, Kind = "rightAngle" });

        return new Diagram { Pages = { page } };
    }

    [Fact]
    public void ServiceStoreGrid_RoutesUnobstructedStoresStraightDownTheirColumn()
    {
        var diagram = ServiceStoreGrid();
        var page = diagram.Pages[0];

        OrthogonalRouter.Route(diagram);

        // The bus occupies x 6.40..10.00, so the customs->es column is genuinely blocked and
        // detouring around it is correct. The outer columns are clear and must go straight down.
        var clearColumns = new Dictionary<string, double>
        {
            ["order"] = 2.60, ["pg"] = 2.60,
            ["pay"] = 5.40, ["redis"] = 5.40,
            ["stock"] = 11.00, ["s3"] = 11.00
        };

        foreach (var edge in page.Edges)
        {
            if (!clearColumns.TryGetValue(edge.From!, out double expected)) continue;

            var points = edge.Waypoints ?? new List<Point>();
            Assert.All(points, p => Assert.True(Math.Abs(p.X - expected) < 0.30,
                $"{edge.Id}: waypoint x={p.X:F2} left its clear column x={expected:F2}"));
        }
    }

    [Fact]
    public void ServiceStoreGrid_BlockedColumnDetoursLocallyNotAcrossThePage()
    {
        var diagram = ServiceStoreGrid();
        OrthogonalRouter.Route(diagram);

        var edge = diagram.Pages[0].Edges.Single(e => e.Id == "customs-es");
        var points = edge.Waypoints ?? new List<Point>();

        // Going around the bus is expected — it blocks x 6.40..10.00, so the route has to pass
        // either side of it. What must not happen is the historical behaviour: traversing to the
        // page edge (x=1.10 or x=15.30) when a lane just past the obstacle is available.
        Assert.All(points, p => Assert.True(p.X > 2.0 && p.X < 14.0,
            $"customs-es detoured to x={p.X:F2}, out to the page edge"));

        // Detour cost should stay near the straight distance, not multiply it.
        double straight = Math.Abs(7.25 - 3.00);
        double routed = 0;
        var previous = new Point(8.20, 7.25);
        foreach (var p in points.Concat(new[] { new Point(8.20, 3.00) }))
        {
            routed += Math.Abs(p.X - previous.X) + Math.Abs(p.Y - previous.Y);
            previous = p;
        }

        Assert.True(routed < straight * 3.5,
            $"customs-es routed {routed:F2}\" vs straight {straight:F2}\" ({routed / straight:F2}x)");
    }

    [Fact]
    public void ServiceStoreGrid_NoEdgeDetoursToThePageEdge()
    {
        var diagram = ServiceStoreGrid();
        OrthogonalRouter.Route(diagram);

        foreach (var edge in diagram.Pages[0].Edges)
        {
            var points = edge.Waypoints ?? new List<Point>();

            // Historic failure: three separate edges all shared x=1.10, a fourth used x=15.30.
            Assert.DoesNotContain(points, p => p.X < 2.0);
            Assert.DoesNotContain(points, p => p.X > 14.0);
        }
    }

    [Fact]
    public void ServiceStoreGrid_EdgesDoNotShareOneChannel()
    {
        var diagram = ServiceStoreGrid();
        OrthogonalRouter.Route(diagram);

        // Collinear duplicate routes draw as a single line, so N edges look like one and the
        // diagram silently loses information.
        var channels = diagram.Pages[0].Edges
            .Where(e => e.Waypoints is { Count: > 0 })
            .Select(e => Math.Round(e.Waypoints![0].X, 1))
            .ToList();

        Assert.Equal(channels.Count, channels.Distinct().Count());
    }

    [Fact]
    public void StackedNodesRouteStraightDownTheirSharedColumn()
    {
        var diagram = StackedPair(x: 8.20, topY: 7.70, bottomY: 2.50);

        OrthogonalRouter.Route(diagram);

        var edge = diagram.Pages[0].Edges[0];
        Assert.NotNull(edge.Waypoints);

        // Every waypoint must stay in the column the two nodes share. A route that leaves
        // x=8.20 is going somewhere it has no reason to go.
        Assert.All(edge.Waypoints!, p =>
            Assert.True(Math.Abs(p.X - 8.20) < 0.30,
                $"waypoint x={p.X:F2} left the shared column at x=8.20"));
    }

    [Fact]
    public void StackedNodesDoNotDetourToThePageEdge()
    {
        var diagram = StackedPair(x: 8.20, topY: 7.70, bottomY: 2.50);

        OrthogonalRouter.Route(diagram);

        var points = diagram.Pages[0].Edges[0].Waypoints ?? new List<Point>();

        // The historic failure put waypoints at 1.10 or 15.30 — the page's first and last lane.
        Assert.DoesNotContain(points, p => p.X < 2.0);
        Assert.DoesNotContain(points, p => p.X > 14.0);
    }

    [Fact]
    public void StackedRouteIsNotLongerThanTheStraightLineByMuch()
    {
        var diagram = StackedPair(x: 8.20, topY: 7.70, bottomY: 2.50);

        OrthogonalRouter.Route(diagram);

        var edge = diagram.Pages[0].Edges[0];
        var points = edge.Waypoints ?? new List<Point>();

        double straight = Math.Abs(7.70 - 2.50);

        double routed = 0;
        var previous = new Point(8.20, 7.70);
        foreach (var p in points.Concat(new[] { new Point(8.20, 2.50) }))
        {
            routed += Math.Abs(p.X - previous.X) + Math.Abs(p.Y - previous.Y);
            previous = p;
        }

        // The measured defect was 3.7x the straight distance. A direct vertical is ~1.0x.
        Assert.True(routed < straight * 1.8,
            $"routed {routed:F2}\" vs straight {straight:F2}\" ({routed / straight:F2}x)");
    }

    [Fact]
    public void StackedNodesInAnotherColumnBehaveTheSameWay()
    {
        // The bug was not specific to the middle of the page; it followed from having no
        // inter-column gap, so a second column position must behave identically.
        var diagram = StackedPair(x: 3.40, topY: 7.70, bottomY: 2.50);

        OrthogonalRouter.Route(diagram);

        var points = diagram.Pages[0].Edges[0].Waypoints ?? new List<Point>();
        Assert.All(points, p => Assert.True(Math.Abs(p.X - 3.40) < 0.30,
            $"waypoint x={p.X:F2} left the shared column at x=3.40"));
    }

    [Fact]
    public void StackedRouteAvoidsAShapeSittingBetweenThem()
    {
        // Direct-down is right only when the column is actually clear. If something blocks it,
        // the router must still find its way around rather than drawing through the obstacle.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 12 },
            Nodes =
            {
                new Node { Id = "top", Text = "Top", Bounds = new Bounds(8.20, 7.70, 2.1, 0.9) },
                new Node { Id = "bottom", Text = "Bottom", Bounds = new Bounds(8.20, 2.50, 2.1, 1.0) },
                new Node { Id = "blocker", Text = "Blocker", Bounds = new Bounds(8.20, 5.10, 2.1, 0.9) }
            },
            Edges = { new Edge { Id = "e", From = "top", To = "bottom", Kind = "rightAngle" } }
        };
        var diagram = new Diagram { Pages = { page } };

        OrthogonalRouter.Route(diagram);

        var points = page.Edges[0].Waypoints ?? new List<Point>();
        var blocker = page.Nodes.Single(n => n.Id == "blocker").Bounds;

        // No segment of the route may pass through the blocker's box.
        var polyline = new List<Point> { new(8.20, 7.25) };
        polyline.AddRange(points);
        polyline.Add(new Point(8.20, 3.00));

        for (int i = 0; i < polyline.Count - 1; i++)
        {
            var a = polyline[i];
            var b = polyline[i + 1];
            double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
            double minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);

            bool overlaps = maxX > blocker.X - blocker.W / 2 && minX < blocker.X + blocker.W / 2
                         && maxY > blocker.Y - blocker.H / 2 && minY < blocker.Y + blocker.H / 2;

            Assert.False(overlaps, $"segment {i} ({a.X:F2},{a.Y:F2})->({b.X:F2},{b.Y:F2}) crosses the blocker");
        }
    }
}
