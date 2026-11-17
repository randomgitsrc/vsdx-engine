using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Computes orthogonal connector routes that avoid unrelated shapes.
///
/// The router works on a set of horizontal "lanes" (one per gap between node rows) and
/// vertical "channels" (one per gap between node columns). A portion of each edge's
/// waypoints is baked into the model so the rendering, the VSDX geometry, and the layout
/// checker all agree about where the line runs.
/// </summary>
public static class OrthogonalRouter
{
    /// <summary>Minimum clearance kept between a route and an unrelated shape.</summary>
    public const double Clearance = 0.16;

    /// <summary>
    /// Assigns <see cref="Edge.Waypoints"/> to every edge whose kind is orthogonal
    /// (rightAngle/orthogonal/elbow) and that connects two nodes.
    /// </summary>
    public static int Route(Diagram diagram)
    {
        int routed = 0;
        foreach (var page in diagram.Pages)
            routed += RoutePage(page);
        return routed;
    }

    private static int RoutePage(Page page)
    {
        var nodes = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        if (nodes.Count == 0) return 0;

        var lanes = ComputeLanes(page, nodes.Values);
        var channels = ComputeChannels(page, nodes.Values);
        int count = 0;

        foreach (var edge in page.Edges)
        {
            if (edge.From is null || edge.To is null) continue;
            if (!nodes.TryGetValue(edge.From, out var from) || !nodes.TryGetValue(edge.To, out var to)) continue;

            // An author-supplied route is authoritative: never overwrite it.
            if (edge.Waypoints is { Count: > 0 }) continue;

            // Straight edges stay straight unless they would cut through another shape;
            // only then do they get an orthogonal detour.
            if (!IsOrthogonal(edge.Kind))
            {
                var others = nodes.Values.Where(n => n.Id != from.Id && n.Id != to.Id).ToList();
                var direct = new List<Point>
                {
                    BoundaryPoint(from.Bounds, to.Bounds.X - from.Bounds.X, to.Bounds.Y - from.Bounds.Y),
                    BoundaryPoint(to.Bounds, from.Bounds.X - to.Bounds.X, from.Bounds.Y - to.Bounds.Y)
                };
                if (!PathHitsShapes(direct, others)) continue;
            }

            var attempt = RouteBetween(from, to, edge, nodes.Values, lanes, channels);

            // If the preferred sides are obstructed, try every other side combination before
            // accepting a route that crosses a shape.
            if (attempt.Blocked)
            {
                foreach (var (fs, ts) in SidePairs(edge.FromPort, edge.ToPort))
                {
                    var retry = new Edge { From = edge.From, To = edge.To, FromPort = fs, ToPort = ts };
                    var candidate = RouteBetween(from, to, retry, nodes.Values, lanes, channels);
                    if (candidate.Blocked) continue;
                    attempt = candidate;
                    break;
                }
            }

            edge.Waypoints = attempt.Waypoints;
            edge.Kind = "rightAngle";
            if (attempt.StartSide is not null) edge.FromPort = attempt.StartSide;
            if (attempt.EndSide is not null) edge.ToPort = attempt.EndSide;
            count++;
        }

        return count;
    }

    private static bool IsOrthogonal(string? kind) =>
        kind is not null && (kind.Equals("rightAngle", StringComparison.OrdinalIgnoreCase)
                          || kind.Equals("right-angle", StringComparison.OrdinalIgnoreCase)
                          || kind.Equals("orthogonal", StringComparison.OrdinalIgnoreCase)
                          || kind.Equals("elbow", StringComparison.OrdinalIgnoreCase));

    /// <summary>Free horizontal bands between node rows, top to bottom.</summary>
    private static List<double> ComputeLanes(Page page, IEnumerable<Node> nodes)
    {
        var bands = nodes
            .Select(n => (top: n.Bounds.Y + n.Bounds.H / 2, bottom: n.Bounds.Y - n.Bounds.H / 2))
            .OrderByDescending(b => b.top)
            .ToList();

        var lanes = new List<double>();
        if (bands.Count == 0) return lanes;

        // Lane above the top row and below the bottom row.
        lanes.Add(bands[0].top + 0.30);
        lanes.Add(bands[^1].bottom - 0.30);

        for (int i = 0; i < bands.Count - 1; i++)
        {
            double lowerTop = bands[i + 1].top;
            double upperBottom = bands[i].bottom;
            if (upperBottom - lowerTop < 0.14) continue;
            lanes.Add((upperBottom + lowerTop) / 2);
        }

        // Snap to a 0.05" grid so routes look deliberate rather than arbitrary.
        return lanes.Select(l => Math.Round(l * 20) / 20.0).Distinct().ToList();
    }

    /// <summary>Free vertical channels between node columns, left to right.</summary>
    private static List<double> ComputeChannels(Page page, IEnumerable<Node> nodes)
    {
        var columns = nodes
            .Select(n => (left: n.Bounds.X - n.Bounds.W / 2, right: n.Bounds.X + n.Bounds.W / 2))
            .OrderBy(c => c.left)
            .ToList();

        var channels = new List<double>();
        if (columns.Count == 0) return channels;

        for (int i = 0; i < columns.Count - 1; i++)
        {
            if (columns[i + 1].left - columns[i].right < 0.14) continue;
            channels.Add((columns[i].right + columns[i + 1].left) / 2);
        }

        return channels.Select(c => Math.Round(c * 20) / 20.0).Distinct().ToList();
    }

    private readonly record struct RouteAttempt(
        List<Point> Waypoints, bool Blocked, string? StartSide = null, string? EndSide = null);

    private static RouteAttempt RouteBetween(
        Node from, Node to, Edge edge, IEnumerable<Node> allNodes,
        List<double> lanes, List<double> channels)
    {
        // Source and target are excluded from obstacle tests: the route necessarily touches them.
        var others = allNodes.Where(n => n.Id != from.Id && n.Id != to.Id).ToList();

        // Anchor on the declared port, with a short perpendicular stub so the line leaves the
        // shape squarely instead of clipping its corner.
        var (p0, d0, side0) = Anchor(from, edge.FromPort, to);
        var (p1, d1, side1) = Anchor(to, edge.ToPort, from);

        const double Stub = 0.26;
        var a = new Point(p0.X + d0.X * Stub, p0.Y + d0.Y * Stub);
        var b = new Point(p1.X + d1.X * Stub, p1.Y + d1.Y * Stub);

        bool fromHorizontal = Math.Abs(d0.X) > 0.5;   // leaves via left/right side
        bool toHorizontal = Math.Abs(d1.X) > 0.5;

        var candidates = new List<List<Point>>();

        // Straight line when both stubs already line up.
        if (fromHorizontal && toHorizontal && Math.Abs(a.Y - b.Y) < 1e-6 && Math.Sign(d0.X) == -Math.Sign(d1.X))
            candidates.Add(new List<Point> { a, b });
        if (!fromHorizontal && !toHorizontal && Math.Abs(a.X - b.X) < 1e-6 && Math.Sign(d0.Y) == -Math.Sign(d1.Y))
            candidates.Add(new List<Point> { a, b });

        // L-shape through the corner that matches both stub directions.
        var corner = fromHorizontal ? new Point(b.X, a.Y) : new Point(a.X, b.Y);
        if ((fromHorizontal && Math.Sign(b.X - a.X) == Math.Sign(d0.X))
         || (!fromHorizontal && Math.Sign(b.Y - a.Y) == Math.Sign(d0.Y)))
            candidates.Add(new List<Point> { a, corner, b });

        // Z-shape via a channel (horizontal stubs) or a lane (vertical stubs).
        if (fromHorizontal && toHorizontal)
        {
            foreach (var channel in channels.OrderBy(c => Math.Abs(c - (a.X + b.X) / 2)))
                candidates.Add(new List<Point> { a, new(channel, a.Y), new(channel, b.Y), b });
        }
        else if (!fromHorizontal && !toHorizontal)
        {
            foreach (var lane in lanes.OrderBy(l => Math.Abs(l - (a.Y + b.Y) / 2)))
                candidates.Add(new List<Point> { a, new(a.X, lane), new(b.X, lane), b });
        }
        else
        {
            // Mixed: one horizontal stub, one vertical stub.
            var mixed = fromHorizontal ? new Point(b.X, a.Y) : new Point(a.X, b.Y);
            candidates.Add(new List<Point> { a, mixed, b });
            foreach (var channel in channels.OrderBy(c => Math.Abs(c - a.X) + Math.Abs(c - b.X)))
            {
                if (fromHorizontal) candidates.Add(new List<Point> { a, new(channel, a.Y), new(channel, b.Y), b });
                else candidates.Add(new List<Point> { a, new(a.X, b.Y), new(channel, b.Y), b });
            }
            foreach (var lane in lanes.OrderBy(l => Math.Abs(l - a.Y) + Math.Abs(l - b.Y)))
            {
                if (fromHorizontal) candidates.Add(new List<Point> { a, new(a.X, lane), new(b.X, lane), b });
                else candidates.Add(new List<Point> { a, new(a.X, lane), new(b.X, lane), b });
            }
        }

        // Two-bend fallback through the widest free lane, used when everything else is blocked.
        foreach (var lane in lanes.OrderBy(l => Math.Abs(l - (a.Y + b.Y) / 2)))
            candidates.Add(new List<Point> { a, new(a.X, lane), new(b.X, lane), b });

        // Free rows immediately below/above the source row and target row: lets a connector run
        // under a crowded band instead of through it.
        foreach (var offset in new[] { 0.34, 0.50, 0.70, 0.95 })
        {
            double below = Math.Min(from.Bounds.Y - from.Bounds.H / 2, to.Bounds.Y - to.Bounds.H / 2) - offset;
            double above = Math.Max(from.Bounds.Y + from.Bounds.H / 2, to.Bounds.Y + to.Bounds.H / 2) + offset;
            candidates.Add(new List<Point>
            {
                new(a.X, Snap(below)), new(b.X, Snap(below))
            });
            candidates.Add(new List<Point>
            {
                new(a.X, Snap(above)), new(b.X, Snap(above))
            });
        }

        // Perimeter routes: leave the cluster entirely, travel outside it, come back. These are
        // the last resort before accepting an obstructed path.
        double left = allNodes.Min(n => n.Bounds.X - n.Bounds.W / 2) - 0.45;
        double right = allNodes.Max(n => n.Bounds.X + n.Bounds.W / 2) + 0.45;
        double bottomY = allNodes.Min(n => n.Bounds.Y - n.Bounds.H / 2) - 0.45;
        double topY = allNodes.Max(n => n.Bounds.Y + n.Bounds.H / 2) + 0.45;
        foreach (var wall in new[] { right, left })
            candidates.Add(new List<Point> { a, new(wall, a.Y), new(wall, b.Y), b });

        // Side-step then drop: leave the row horizontally into a clear channel, drop to a free
        // row, then approach the target. This is what a human draws when the source sits
        // directly above an obstacle (the common "diagonal across bands" case).
        foreach (var channel in channels)
        foreach (var lane in lanes)
        {
            candidates.Add(new List<Point>
            {
                a, new(channel, a.Y), new(channel, lane), new(b.X, lane), b
            });
            candidates.Add(new List<Point>
            {
                a, new(a.X, lane), new(channel, lane), new(channel, b.Y), b
            });
        }
        foreach (var wall in new[] { topY, bottomY })
            candidates.Add(new List<Point> { a, new(a.X, wall), new(b.X, wall), b });

        // Among all clear candidates prefer the shortest polyline with the fewest bends.
        List<Point>? best = null;
        double bestCost = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            var path = Simplify(candidate);
            if (path.Count < 2) continue;

            // Test the whole drawn polyline: from the shape boundary, through the stub, to the
            // anchor. Validating only a..b would miss a stub that cuts through a neighbour.
            var full = new List<Point> { p0 };
            full.AddRange(path);
            full.Add(p1);
            full = Simplify(full);
            if (PathHitsShapes(full, others)) continue;

            double length = 0;
            for (int i = 0; i < path.Count - 1; i++) length += Distance(path[i], path[i + 1]);
            double cost = length + (path.Count - 2) * 0.35;


            if (cost < bestCost)
            {
                bestCost = cost;
                best = path;
            }
        }

        if (best is not null)
            return new RouteAttempt(StripEnds(best, a, b), false, side0, side1);

        // Nothing was clear. Emit the least-bad route but still report the sides used, so the
        // checker anchors its measurement to the same geometry the renderer will draw.
        double fallback = PickLane(lanes, from.Bounds.Y, to.Bounds.Y, a, b, others);
        return new RouteAttempt(
            StripEnds(Simplify(new List<Point> { a, new(a.X, fallback), new(b.X, fallback), b }), a, b),
            true, side0, side1);
    }

    /// <summary>
    /// The waypoints must exclude the connector's own endpoints: OfficeIMO anchors those to the
    /// port and would otherwise draw a duplicate segment back to the anchor point.
    /// </summary>
    private static List<Point> StripEnds(List<Point> path, Point a, Point b)
    {
        var inner = path.Where(p => !(Math.Abs(p.X - a.X) < 1e-9 && Math.Abs(p.Y - a.Y) < 1e-9)
                                 && !(Math.Abs(p.X - b.X) < 1e-9 && Math.Abs(p.Y - b.Y) < 1e-9)).ToList();
        return inner;
    }

    /// <summary>Port anchor point plus the outward unit direction.</summary>
    private static (Point point, Point direction, string side) Anchor(Node node, string? port, Node peer)
    {
        var b = node.Bounds;
        switch ((port ?? "auto").ToLowerInvariant())
        {
            case "left": return (new Point(b.X - b.W / 2, b.Y), new Point(-1, 0), "left");
            case "right": return (new Point(b.X + b.W / 2, b.Y), new Point(1, 0), "right");
            case "top": return (new Point(b.X, b.Y + b.H / 2), new Point(0, 1), "top");
            case "bottom": return (new Point(b.X, b.Y - b.H / 2), new Point(0, -1), "bottom");
        }

        // Auto: pick the side facing the peer, preferring the dominant axis.
        double dx = peer.Bounds.X - b.X, dy = peer.Bounds.Y - b.Y;
        double scaleX = Math.Abs(dx) < 1e-9 ? double.PositiveInfinity : (b.W / 2) / Math.Abs(dx);
        double scaleY = Math.Abs(dy) < 1e-9 ? double.PositiveInfinity : (b.H / 2) / Math.Abs(dy);
        return Math.Abs(dx) * scaleY > Math.Abs(dy) * scaleX
            ? (new Point(b.X + Math.Sign(dx) * b.W / 2, b.Y), new Point(Math.Sign(dx), 0), dx > 0 ? "right" : "left")
            : (new Point(b.X, b.Y + Math.Sign(dy) * b.H / 2), new Point(0, Math.Sign(dy)), dy > 0 ? "top" : "bottom");
    }

    private static double Snap(double value) => Math.Round(value * 20) / 20.0;

    /// <summary>All side combinations, preferred first.</summary>
    private static IEnumerable<(string? from, string? to)> SidePairs(string? preferredFrom, string? preferredTo)
    {
        string[] sides = { "bottom", "top", "right", "left" };
        yield return (preferredFrom, preferredTo);

        foreach (var fs in new[] { preferredFrom, "right", "left", "bottom", "top" }.OfType<string>().Distinct())
        foreach (var ts in new[] { preferredTo, "top", "bottom", "left", "right" }.OfType<string>().Distinct())
        {
            if (fs == preferredFrom && ts == preferredTo) continue;
            yield return (fs, ts);
        }
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static List<Point> Simplify(List<Point> path)
    {
        var result = new List<Point>();
        foreach (var point in path)
        {
            if (result.Count > 0 &&
                Math.Abs(result[^1].X - point.X) < 1e-9 &&
                Math.Abs(result[^1].Y - point.Y) < 1e-9)
                continue;
            result.Add(point);
        }
        return result;
    }

    /// <summary>
    /// Routes leave vertically/horizontally, cross in a free channel, and arrive from the
    /// destination's own side. Returns null when no channel is clear.
    /// </summary>
    private static List<Point>? TryChannelRoute(
        Node from, Node to, List<double> channels, List<Node> others)
    {
        if (channels.Count == 0) return null;

        // Start on the bottom/top edge of the source row, end on the top/bottom of the target.
        var start = new Point(from.Bounds.X, from.Bounds.Y);
        var end = new Point(to.Bounds.X, to.Bounds.Y);

        // Candidate channels ordered by total detour length.
        foreach (var channel in channels.OrderBy(c => Math.Abs(c - start.X) + Math.Abs(c - end.X)))
        {
            var path = new List<Point>
            {
                new(start.X, from.Bounds.Y - from.Bounds.H / 2),
                new(start.X, MidLaneY(from, to)),
                new(channel, MidLaneY(from, to)),
                new(channel, end.Y),
                new(end.X, end.Y)
            };

            if (!PathHitsShapes(path, others)) return path;
        }

        return null;
    }

    /// <summary>A Y coordinate in the gap between the two rows.</summary>
    private static double MidLaneY(Node from, Node to)
    {
        double fromBottom = from.Bounds.Y - from.Bounds.H / 2;
        double toTop = to.Bounds.Y + to.Bounds.H / 2;
        if (toTop < fromBottom) return Math.Round(((fromBottom + toTop) / 2) * 20) / 20.0;
        return Math.Round((fromBottom - 0.35) * 20) / 20.0;
    }

    private static double PickLane(
        List<double> lanes, double fromY, double toY, Point start, Point end, List<Node> others)
    {
        // Prefer a lane strictly between the two rows; fall back to the nearest one.
        double low = Math.Min(fromY, toY), high = Math.Max(fromY, toY);
        var between = lanes.Where(l => l < high - 0.05 && l > low + 0.05).ToList();

        List<double> candidates = between.Count > 0
            ? between.OrderBy(l => Math.Abs(l - (fromY + toY) / 2)).ToList()
            : lanes.OrderBy(l => Math.Abs(l - (fromY + toY) / 2)).ToList();

        foreach (var lane in candidates)
        {
            var path = new List<Point> { start, new(start.X, lane), new(end.X, lane), end };
            if (!PathHitsShapes(path, others)) return lane;
        }

        return candidates.FirstOrDefault((fromY + toY) / 2);
    }

    private static double PickChannel(
        List<double> channels, double fromX, double toX, Point start, Point end, List<Node> others)
    {
        double low = Math.Min(fromX, toX), high = Math.Max(fromX, toX);
        var between = channels.Where(c => c > low + 0.05 && c < high - 0.05).ToList();

        List<double> candidates = between.Count > 0
            ? between.OrderBy(c => Math.Abs(c - (fromX + toX) / 2)).ToList()
            : channels.OrderBy(c => Math.Abs(c - (fromX + toX) / 2)).ToList();

        foreach (var channel in candidates)
        {
            var path = new List<Point> { start, new(channel, start.Y), new(channel, end.Y), end };
            if (!PathHitsShapes(path, others)) return channel;
        }

        return candidates.FirstOrDefault((fromX + toX) / 2);
    }

    /// <summary>Is a straight run between two shapes blocked by an unrelated shape?</summary>
    private static bool Blocked(Bounds from, Bounds to, List<Node> others, bool horizontalRun)
    {
        if (horizontalRun)
        {
            double y = (from.Y + to.Y) / 2;
            double x1 = from.X, x2 = to.X;
            double lo = Math.Min(x1, x2), hi = Math.Max(x1, x2);
            foreach (var node in others)
            {
                if (node.Bounds.Y - node.Bounds.H / 2 - Clearance > y) continue;
                if (node.Bounds.Y + node.Bounds.H / 2 + Clearance < y) continue;
                if (node.Bounds.X + node.Bounds.W / 2 < lo || node.Bounds.X - node.Bounds.W / 2 > hi) continue;
                return true;
            }
            return false;
        }

        double x = (from.X + to.X) / 2;
        double y1 = from.Y, y2 = to.Y;
        double ylo = Math.Min(y1, y2), yhi = Math.Max(y1, y2);
        foreach (var node in others)
        {
            if (node.Bounds.X - node.Bounds.W / 2 - Clearance > x) continue;
            if (node.Bounds.X + node.Bounds.W / 2 + Clearance < x) continue;
            if (node.Bounds.Y + node.Bounds.H / 2 < ylo || node.Bounds.Y - node.Bounds.H / 2 > yhi) continue;
            return true;
        }
        return false;
    }

    private static bool PathHitsShapes(List<Point> path, List<Node> others)
    {
        for (int i = 0; i < path.Count - 1; i++)
        {
            var a = path[i];
            var b = path[i + 1];
            foreach (var node in others)
            {
                double x1 = node.Bounds.X - node.Bounds.W / 2 - Clearance;
                double x2 = node.Bounds.X + node.Bounds.W / 2 + Clearance;
                double y1 = node.Bounds.Y - node.Bounds.H / 2 - Clearance;
                double y2 = node.Bounds.Y + node.Bounds.H / 2 + Clearance;
                if (SegmentIntersectsRect(a, b, x1, y1, x2, y2)) return true;
            }
        }
        return false;
    }

    private static bool SegmentIntersectsRect(Point a, Point b, double x1, double y1, double x2, double y2)
    {
        // Trivial reject for axis-aligned segments, which is all the router emits.
        if (Math.Abs(a.X - b.X) < 1e-9)
        {
            double x = a.X;
            if (x < x1 || x > x2) return false;
            double lo = Math.Min(a.Y, b.Y), hi = Math.Max(a.Y, b.Y);
            return hi >= y1 && lo <= y2;
        }
        if (Math.Abs(a.Y - b.Y) < 1e-9)
        {
            double y = a.Y;
            if (y < y1 || y > y2) return false;
            double lo = Math.Min(a.X, b.X), hi = Math.Max(a.X, b.X);
            return hi >= x1 && lo <= x2;
        }

        double dx = b.X - a.X, dy = b.Y - a.Y;
        double t0 = 0, t1 = 1;
        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-12) return q >= 0;
            double r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
        return Clip(-dx, a.X - x1) && Clip(dx, x2 - a.X) && Clip(-dy, a.Y - y1) && Clip(dy, y2 - a.Y);
    }

    private static Point BoundaryPoint(Bounds b, double dx, double dy)
    {
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return new Point(b.X, b.Y);
        double halfW = b.W / 2, halfH = b.H / 2;
        double scaleX = Math.Abs(dx) < 1e-9 ? double.PositiveInfinity : halfW / Math.Abs(dx);
        double scaleY = Math.Abs(dy) < 1e-9 ? double.PositiveInfinity : halfH / Math.Abs(dy);
        double scale = Math.Min(scaleX, scaleY);
        return new Point(b.X + dx * scale, b.Y + dy * scale);
    }
}
