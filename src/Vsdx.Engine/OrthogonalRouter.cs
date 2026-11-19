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
        _currentPage = page;
        ResetSideUse();

        var nodes = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        if (nodes.Count == 0) return 0;

        var lanes = ComputeLanes(page, nodes.Values);
        var channels = ComputeChannels(page, nodes.Values);
        int count = 0;

        // Fan-outs first: three or more connectors leaving one node for targets that sit side by side are routed as
        // one bus. Routed independently they take different shapes — measured on `slack-cellular`, one path
        // detouring twice while the other two were straight diagonals — and a reader cannot tell they are the same
        // decision. A bus gives every branch the same shape by construction.
        count += RouteFanOuts(page, nodes);

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
                    ShapeGeometry.OutlinePoint(from.Bounds, from.Shape,
                        to.Bounds.X - from.Bounds.X, to.Bounds.Y - from.Bounds.Y),
                    ShapeGeometry.OutlinePoint(to.Bounds, to.Shape,
                        from.Bounds.X - to.Bounds.X, from.Bounds.Y - to.Bounds.Y)
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
        var (p0, d0, side0) = Anchor(from, edge.FromPort, to, others);
        var (p1, d1, side1) = Anchor(to, edge.ToPort, from, others);

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
            // Both ends leave vertically. When they also share a column the honest answer is a
            // straight drop between them, and trying that first matters: the channel list only
            // contains gaps *between* columns, so a same-column pair has no channel of its own and
            // used to fall through to the outermost lane on the page.
            if (Math.Abs(a.X - b.X) < 0.5)
                candidates.Add(new List<Point> { a, b });

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

            // A container's heading is ink: routing through it draws a line over the band's title. The node list
            // `others` does not contain containers, so the heading band has to be tested separately.
            if (CrossesHeading(from, to, full)) continue;

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
        //
        // `PickLane` now prefers a lane that is actually clear. That matters because this is the branch that runs
        // when every candidate above failed: if it also ignores obstacles, a connector goes through a node and the
        // hard constraint fires — which is exactly what happened when anchor offsets pushed every candidate into
        // the blocked set and the fallback drew through a neighbour.
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

    /// <summary>
    /// The side whose straight line to <paramref name="peer"/> crosses no other node, preferring
    /// <paramref name="preferred"/> when several qualify.
    ///
    /// A side whose first stretch is blocked is never returned unless every side is blocked — in that case the
    /// caller's preference stands and the candidate search downstream deals with it.
    /// </summary>
    private static string BestClearSide(Node node, Node peer, List<Node> others, string preferred)
    {
        if (others.Count == 0) return preferred;

        var b = node.Bounds;
        var target = new Point(peer.Bounds.X, peer.Bounds.Y);

        var clear = new List<(string Side, double Error)>();

        foreach (var candidate in new[] { "left", "right", "top", "bottom" })
        {
            var (dx, dy) = candidate switch
            {
                "left" => (-1.0, 0.0),
                "right" => (1.0, 0.0),
                "top" => (0.0, 1.0),
                _ => (0.0, -1.0)
            };

            var from = ShapeGeometry.OutlinePoint(b, node.Shape, dx, dy);
            var path = new List<Point> { from, target };

            if (others.Any(o => PathCrossesBox(path, o.Bounds))) continue;

            // How far this side's outward normal is from the direction of the peer, as a cosine distance.
            double px = target.X - b.X, py = target.Y - b.Y;
            double norm = Math.Sqrt(px * px + py * py);
            if (norm < 1e-9) return candidate;

            double alignment = (px * dx + py * dy) / norm;
            clear.Add((candidate, 1.0 - alignment));
        }

        if (clear.Count == 0) return preferred;

        return clear.OrderBy(c => Math.Abs(c.Side == preferred ? 0 : 0.001)).ThenBy(c => c.Error).First().Side;
    }

    /// <summary>
    /// How far along its side the nth connector on that side sits, as a fraction of the side, in -0.30 .. +0.30.
    ///
    /// The first connector stays at the midpoint; later ones alternate. Bounded so a connector never leaves from a
    /// corner, where a shape's outline may not reach.
    /// </summary>
    private static double SpreadOffset(Node node, string side)
    {
        var key = node.Id + "|" + side;
        int used = _sideUse.TryGetValue(key, out var n) ? n : 0;
        _sideUse[key] = used + 1;

        return used switch { 0 => 0.0, 1 => 0.18, 2 => -0.18, 3 => 0.30, 4 => -0.30, _ => 0.0 };
    }

    /// <summary>How many connectors have left each node from each side, cleared per routing pass.</summary>
    private static readonly Dictionary<string, int> _sideUse = new(StringComparer.Ordinal);

    private static void ResetSideUse() => _sideUse.Clear();

    /// <summary>Port anchor point plus the outward unit direction.</summary>
    /// <param name="others">
    /// Every node neither end of this connector. Available so a side can be chosen with the obstacles in view;
    /// the auto case does not consult it yet, which is why a connector can still be routed through a neighbour.
    /// </param>
    private static (Point point, Point direction, string side) Anchor(
        Node node, string? port, Node peer, List<Node> others)
    {
        var b = node.Bounds;
        switch ((port ?? "auto").ToLowerInvariant())
        {
            // Explicit sides follow the shape's outline, not its box. On a parallelogram or diamond the box
            // edge is empty space at the middle of a side, so a connector anchored there stops short of the ink.
            case "left": return (ShapeGeometry.OutlinePoint(b, node.Shape, -1, 0), new Point(-1, 0), "left");
            case "right": return (ShapeGeometry.OutlinePoint(b, node.Shape, 1, 0), new Point(1, 0), "right");
            case "top": return (ShapeGeometry.OutlinePoint(b, node.Shape, 0, 1), new Point(0, 1), "top");
            case "bottom": return (ShapeGeometry.OutlinePoint(b, node.Shape, 0, -1), new Point(0, -1), "bottom");
        }

        // Auto: pick the side facing the peer, preferring the dominant axis.
        double dx = peer.Bounds.X - b.X, dy = peer.Bounds.Y - b.Y;
        double scaleX = Math.Abs(dx) < 1e-9 ? double.PositiveInfinity : (b.W / 2) / Math.Abs(dx);
        double scaleY = Math.Abs(dy) < 1e-9 ? double.PositiveInfinity : (b.H / 2) / Math.Abs(dy);
        bool horizontal = Math.Abs(dx) * scaleY > Math.Abs(dy) * scaleX;
        string side = horizontal ? (dx > 0 ? "right" : "left") : (dy > 0 ? "top" : "bottom");

        // Direction alone can pick the worst of the four sides. Measured on `d4` in `deploy-topology`: the peer is
        // south-west, so direction says `left`, and that line runs through the WAF beside it — while `right` and
        // `bottom` are both clear.
        //
        // Every side is scored by whether a straight line from its anchor to the peer hits another node, and the
        // side that points most nearly at the peer wins among the clear ones. This is the choice OfficeIMO's own
        // `Auto` was making correctly; computing it here means the router and the writer agree instead of one
        // overriding the other.
        side = BestClearSide(node, peer, others, side);

        // Both branches go through `OutlinePoint`, like the explicit sides above. The auto branch used to compute
        // the box edge directly, so on a diamond or hexagon it anchored in empty space while an explicit port on
        // the same shape anchored correctly — the same node, two answers.
        //
        // Connectors sharing a side are spread along it: three edges arriving from the west otherwise land on one
        // pixel. The offset is a fraction of the side, bounded to its middle half so nothing leaves from a corner.
        double offset = SpreadOffset(node, side);

        return horizontal
            ? (ShapeGeometry.OutlinePoint(b, node.Shape, Math.Sign(dx), offset * Math.Sign(dx) * b.H / Math.Max(b.W, 1e-6)),
               new Point(Math.Sign(dx), 0), side)
            : (ShapeGeometry.OutlinePoint(b, node.Shape, offset * Math.Sign(dy) * b.W / Math.Max(b.H, 1e-6), Math.Sign(dy)),
               new Point(0, Math.Sign(dy)), side);
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

            // The fallback is the last resort, so it must not be blind to headings — otherwise "no clear
            // candidate" quietly produces the very crossing the search was avoiding.
            if (PathHitsShapes(path, others)) continue;
            if (_currentPage is not null && CrossesHeading(_currentPage, path)) continue;

            return lane;
        }

        // Nothing between or near the two rows is clear of ink. Route outside the drawing instead of accepting a
        // path through a heading: leaving the page margin and coming back is ugly, a line through a band title is
        // wrong.
        if (_currentPage is not null)
        {
            var page = _currentPage;
            double top = page.Nodes.Max(n => n.Bounds.Y + n.Bounds.H / 2) + 0.45;
            double bottom = page.Nodes.Min(n => n.Bounds.Y - n.Bounds.H / 2) - 0.45;

            foreach (var outside in new[] { top, bottom, (fromY + toY) / 2 })
            {
                var path = new List<Point> { start, new(start.X, outside), new(end.X, outside), end };
                if (!PathHitsShapes(path, others) && !CrossesHeading(page, path)) return outside;
            }
        }

        // Every lane and both page margins failed. Rather than return a lane known to cut through something,
        // return the one that cuts through least: this branch has to produce a path, but it can produce the least
        // bad one, and the caller records the attempt as blocked either way.
        var all = lanes.Count > 0 ? lanes : new List<double> { (fromY + toY) / 2 };

        double leastBad = all[0];
        int fewest = int.MaxValue;

        foreach (var lane in all)
        {
            var path = new List<Point> { start, new(start.X, lane), new(end.X, lane), end };
            int hits = others.Count(n => PathHitsShapes(path, new List<Node> { n }));

            if (hits < fewest)
            {
                fewest = hits;
                leastBad = lane;
            }

            if (hits == 0) break;
        }

        return leastBad;
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

    /// <summary>
    /// Routes every fan-out on the page as a bus, and returns how many edges it handled.
    ///
    /// A fan-out is three or more connectors leaving one node whose targets sit side by side. Routed one at a time
    /// they diverge: the middle target gets a straight line, the outer ones get diagonal or detouring paths, and the
    /// set stops reading as one decision. Measured on `slack-cellular`, the connector to the leftmost cell went out
    /// to (6.15, 4.38), across to (2.4, 4.38), then down — while the other two were straight.
    ///
    /// The bus gives every branch the same shape: one horizontal run at a shared height, then a vertical drop into
    /// each target. Edges that already carry waypoints are left alone; a route the caller supplied is authoritative.
    /// </summary>
    private static int RouteFanOuts(Page page, Dictionary<string, Node> nodes)
    {
        int routed = 0;

        var groups = page.Edges
            .Where(e => e.From is not null && e.To is not null)
            .Where(e => e.Waypoints is null or { Count: 0 })
            .Where(e => nodes.ContainsKey(e.From!) && nodes.ContainsKey(e.To!))
            .GroupBy(e => e.From!, StringComparer.Ordinal)
            .Where(g => g.Count() >= 3)
            .ToList();

        foreach (var group in groups)
        {
            var source = nodes[group.Key];
            var targets = group.Select(e => nodes[e.To!]).ToList();

            // Only a horizontal fan-out: the targets must form a row below (or above) the source. A cluster of
            // targets in different rows is a different picture and the bus would cut through them.
            double targetY = targets.Average(t => t.Bounds.Y);
            bool sameRow = targets.All(t => Math.Abs(t.Bounds.Y - targetY) < 0.30);
            if (!sameRow) continue;

            bool below = targetY < source.Bounds.Y;
            if (!below && targetY <= source.Bounds.Y) continue;

            // Bus height: just outside the source, on the way to the targets.
            double busY = below
                ? source.Bounds.Y - source.Bounds.H / 2 - 0.30
                : source.Bounds.Y + source.Bounds.H / 2 + 0.30;

            foreach (var edge in group)
            {
                var target = nodes[edge.To!];

                // Drop vertically into the target's near edge, from the bus. The horizontal run is implied by the
                // source's stub plus this segment, so only one waypoint is needed per branch.
                double entryY = below
                    ? target.Bounds.Y + target.Bounds.H / 2 + Clearance
                    : target.Bounds.Y - target.Bounds.H / 2 - Clearance;

                // Branches leave the source at different points along its edge. `SpreadOffset` already exists for
                // this — it is what keeps three connectors from drawing a knot at one pixel — but this method wrote
                // waypoints directly and never consulted it. Measured on `slack-cellular`: all three branches out of
                // `Envoy` began at (7.05, 2.65).
                double stemX = source.Bounds.X
                             + SpreadOffset(source, below ? "bottom" : "top") * source.Bounds.W;

                var path = new List<Point>
                {
                    new(stemX, busY),
                    new(target.Bounds.X, busY),
                    new(target.Bounds.X, entryY)
                };

                // Never overwrite a route that would cut through a node: fall back to the normal pass for this one.
                var others = nodes.Values.Where(n => n.Id != source.Id && n.Id != target.Id).ToList();
                if (PathHitsShapes(path, others)) continue;

                edge.Waypoints = Simplify(path);
                edge.Kind = "rightAngle";

                routed++;
            }
        }

        return routed;
    }

    /// <summary>
    /// True when a candidate path passes through a container heading band on the page.
    ///
    /// The heading occupies the top `ContainerHeading` inches of a container. Text is drawn there, so a route
    /// through it puts a line across the title. `PathHitsShapes` cannot catch this: it iterates nodes, and a
    /// container is not a node.
    ///
    /// Only containers that have a name are considered — an unnamed container draws no heading, so its top strip
    /// is empty and blocking it would push routes around nothing.
    /// </summary>
    /// <summary>True when any segment of <paramref name="path"/> intersects the box.</summary>
    private static bool PathCrossesBox(List<Point> path, Bounds box)
    {
        double x1 = box.X - box.W / 2, x2 = box.X + box.W / 2;
        double y1 = box.Y - box.H / 2, y2 = box.Y + box.H / 2;

        for (int i = 0; i < path.Count - 1; i++)
            if (SegmentIntersectsRect(path[i], path[i + 1], x1, y1, x2, y2)) return true;

        return false;
    }

    public static bool CrossesHeading(Page page, List<Point> path)
    {
        foreach (var group in page.Groups)
        {
            if (group.Bounds is null || string.IsNullOrWhiteSpace(group.Name)) continue;

            var g = group.Bounds;
            double top = g.Y + g.H / 2;
            double bottom = top - DiagramLayout.ContainerHeading;

            // The heading text runs from the container's left padding for its measured width.
            double left = g.X - g.W / 2 + DiagramLayout.ContainerPaddingX;
            var measured = TextLayout.Measure(group.Name, 12, bold: true);
            double right = Math.Min(g.X + g.W / 2, left + measured.RequiredWidth);

            if (right <= left) continue;

            var box = new Bounds((left + right) / 2, (top + bottom) / 2, right - left, top - bottom);
            if (PathCrossesBox(path, box)) return true;
        }

        return false;
    }

    /// <summary>
    /// The router only knows the two nodes it is connecting, so it reads the page from them.
    /// </summary>
    private static bool CrossesHeading(Node from, Node to, List<Point> path)
    {
        var page = _currentPage;
        if (page is null) return false;

        foreach (var group in page.Groups)
        {
            if (group.Bounds is null || string.IsNullOrWhiteSpace(group.Name)) continue;

            // A heading only matters if the route could plausibly touch it.
            var g = group.Bounds;
            double top = g.Y + g.H / 2;
            double bottom = top - DiagramLayout.ContainerHeading;

            if (path.All(p => p.Y < bottom || p.Y > top)) continue;

            double left = g.X - g.W / 2 + DiagramLayout.ContainerPaddingX;
            var measured = TextLayout.Measure(group.Name, 12, bold: true);
            double right = Math.Min(g.X + g.W / 2, left + measured.RequiredWidth);
            if (right <= left) continue;

            var box = new Bounds((left + right) / 2, (top + bottom) / 2, right - left, top - bottom);
            if (PathCrossesBox(path, box)) return true;
        }

        return false;
    }

    /// <summary>The page currently being routed, set by <see cref="RoutePage"/> for the duration.</summary>
    [ThreadStatic] private static Page? _currentPage;

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
