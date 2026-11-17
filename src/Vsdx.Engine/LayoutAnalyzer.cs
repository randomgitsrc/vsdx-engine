using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Detects the layout defects that make a diagram look unprofessional. Renders and file
/// validity do not catch these, so the agent loop uses this report instead of eyeballing
/// "the file was produced".
/// </summary>
public static class LayoutAnalyzer
{
    public static LayoutReport Analyze(Diagram diagram, LayoutAnalyzerOptions? options = null)
    {
        options ??= new LayoutAnalyzerOptions();
        var report = new LayoutReport();

        // Route orthogonal edges first so the checker measures the paths that will be drawn.
        OrthogonalRouter.Route(diagram);

        foreach (var page in diagram.Pages)
            AnalyzePage(page, options, report);

        return report;
    }

    private static void AnalyzePage(Page page, LayoutAnalyzerOptions options, LayoutReport report)
    {
        var nodes = page.Nodes;
        if (nodes.Count == 0) return;

        // ---- 1. Node overlap ------------------------------------------------
        for (int i = 0; i < nodes.Count; i++)
        for (int j = i + 1; j < nodes.Count; j++)
        {
            var a = nodes[i];
            var b = nodes[j];
            if (!Overlaps(a.Bounds, b.Bounds, options.OverlapTolerance)) continue;
            report.Add(new LayoutIssue
            {
                Code = "node.overlap",
                Severity = "error",
                Page = page.Name,
                Targets = new[] { a.Id, b.Id },
                Message = $"节点 '{a.Id}' 与 '{b.Id}' 重叠"
            });
        }

        // ---- 2. Node outside the page --------------------------------------
        double pw = page.Size?.Width ?? 0, ph = page.Size?.Height ?? 0;
        if (pw > 0 && ph > 0)
        {
            foreach (var node in nodes)
            {
                if (node.Bounds.X - node.Bounds.W / 2 < -0.01 ||
                    node.Bounds.Y - node.Bounds.H / 2 < -0.01 ||
                    node.Bounds.X + node.Bounds.W / 2 > pw + 0.01 ||
                    node.Bounds.Y + node.Bounds.H / 2 > ph + 0.01)
                {
                    report.Add(new LayoutIssue
                    {
                        Code = "node.out-of-page",
                        Severity = "warning",
                        Page = page.Name,
                        Targets = new[] { node.Id },
                        Message = $"节点 '{node.Id}' 超出页面边界 ({pw}x{ph})"
                    });
                }
            }
        }

        // ---- 3. Text overflow (estimated) ----------------------------------
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Text)) continue;
            var estimate = TextMetrics.Estimate(node.Text!, node);
            if (estimate.RequiredHeightInches > node.Bounds.H + 0.02)
            {
                report.Add(new LayoutIssue
                {
                    Code = "text.overflow-vertical",
                    Severity = "warning",
                    Page = page.Name,
                    Targets = new[] { node.Id },
                    Message = $"'{node.Id}' 文字需要约 {estimate.RequiredHeightInches:F2}\" 高度，实际 {node.Bounds.H:F2}\""
                });
            }
            if (estimate.LongestLineInches > node.Bounds.W + 0.02)
            {
                report.Add(new LayoutIssue
                {
                    Code = "text.overflow-horizontal",
                    Severity = "warning",
                    Page = page.Name,
                    Targets = new[] { node.Id },
                    Message = $"'{node.Id}' 最长行约 {estimate.LongestLineInches:F2}\" 宽，实际 {node.Bounds.W:F2}\""
                });
            }
        }

        // ---- 4. Edges passing through unrelated nodes -----------------------
        var nodeById = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var edge in page.Edges)
        {
            if (edge.From is null || edge.To is null) continue;
            if (!nodeById.TryGetValue(edge.From, out var from)) continue;
            if (!nodeById.TryGetValue(edge.To, out var to)) continue;

            var path = RoutePoints(from, to, edge);
            foreach (var node in nodes)
            {
                if (node.Id == from.Id || node.Id == to.Id) continue;
                if (SegmentIntersectsRect(path, node.Bounds, options.NodeClearance))
                {
                    report.Add(new LayoutIssue
                    {
                        Code = "edge.through-node",
                        Severity = "error",
                        Page = page.Name,
                        Targets = new[] { edge.Id, node.Id },
                        Message = $"连线 '{edge.Id}' ({edge.From} → {edge.To}) 穿过无关节点 '{node.Id}'"
                    });
                }
            }
        }

        // ---- 5. Edge label collision with nodes ----------------------------
        foreach (var edge in page.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.Label)) continue;
            if (edge.From is null || edge.To is null) continue;
            if (!nodeById.TryGetValue(edge.From, out var from) || !nodeById.TryGetValue(edge.To, out var to)) continue;

            var path = RoutePoints(from, to, edge);
            if (path.Count < 2) continue;

            var labelCenter = PointAlong(path, 0.5);
            var halfW = Math.Max(0.35, edge.Label.Length * 0.075);
            var labelBox = new Bounds(labelCenter.X, labelCenter.Y, halfW, 0.16);

            foreach (var node in nodes)
            {
                if (node.Id == from.Id || node.Id == to.Id) continue;
                if (RectOverlapsRect(labelBox, node.Bounds, 0.02))
                {
                    report.Add(new LayoutIssue
                    {
                        Code = "label.overlaps-node",
                        Severity = "warning",
                        Page = page.Name,
                        Targets = new[] { edge.Id, node.Id },
                        Message = $"连线标签 '{edge.Label}' 落在节点 '{node.Id}' 上"
                    });
                }
            }
        }

        // ---- 6. Edge crossings ---------------------------------------------
        var edges = page.Edges
            .Where(e => e.From is not null && e.To is not null
                     && nodeById.ContainsKey(e.From!) && nodeById.ContainsKey(e.To!))
            .Select(e => (edge: e, path: RoutePoints(nodeById[e.From!], nodeById[e.To!], e)))
            .ToList();

        for (int i = 0; i < edges.Count; i++)
        for (int j = i + 1; j < edges.Count; j++)
        {
            var (ea, pa) = edges[i];
            var (eb, pb) = edges[j];
            if (ea.From == eb.From && ea.To == eb.To) continue;
            if (SharesEndpoint(ea, eb)) continue;
            if (PathsCross(pa, pb))
            {
                report.Add(new LayoutIssue
                {
                    Code = "edge.crossing",
                    Severity = "warning",
                    Page = page.Name,
                    Targets = new[] { ea.Id, eb.Id },
                    Message = $"连线 '{ea.Id}' 与 '{eb.Id}' 交叉"
                });
            }
        }

        // ---- 7. Group containment ------------------------------------------
        foreach (var group in page.Groups)
        {
            if (group.Bounds is null) continue;
            foreach (var memberId in group.Members)
            {
                if (!nodeById.TryGetValue(memberId, out var node)) continue;
                if (!RectOverlapsRect(node.Bounds, group.Bounds, 0) ||
                    !Encloses(group.Bounds, node.Bounds, options.OverlapTolerance))
                {
                    report.Add(new LayoutIssue
                    {
                        Code = "group.member-escapes",
                        Severity = "error",
                        Page = page.Name,
                        Targets = new[] { group.Id, memberId },
                        Message = $"节点 '{memberId}' 未被分组 '{group.Id}' 完整包裹"
                    });
                }
            }
        }

        // ---- 8. Spacing ------------------------------------------------------
        CheckSpacing(nodes, options, page, report);

        // ---- 9. Page utilisation ---------------------------------------------
        if (pw > 0 && ph > 0)
        {
            double minX = nodes.Min(n => n.Bounds.X - n.Bounds.W / 2);
            double maxX = nodes.Max(n => n.Bounds.X + n.Bounds.W / 2);
            double minY = nodes.Min(n => n.Bounds.Y - n.Bounds.H / 2);
            double maxY = nodes.Max(n => n.Bounds.Y + n.Bounds.H / 2);

            double contentArea = (maxX - minX) * (maxY - minY);
            double pageArea = pw * ph;
            double ratio = pageArea > 0 ? contentArea / pageArea : 0;

            report.PageUtilisation[page.Name] = ratio;
            report.Bounds[page.Name] = new Bounds((minX + maxX) / 2, (minY + maxY) / 2, maxX - minX, maxY - minY);

            if (ratio < options.MinPageUtilisation)
            {
                report.Add(new LayoutIssue
                {
                    Code = "page.underused",
                    Severity = "info",
                    Page = page.Name,
                    Message = $"图形只占页面的 {ratio:P0}；考虑缩小页面或加大间距"
                });
            }

            // Excessive whitespace between the content box and page edges.
            if (minX > 1.0 || minY > 1.0 || (pw - maxX) > 1.0 || (ph - maxY) > 1.0)
            {
                report.Add(new LayoutIssue
                {
                    Code = "page.margins",
                    Severity = "info",
                    Page = page.Name,
                    Message = $"内容边距偏大 左{minX:F2} 下{minY:F2} 右{pw - maxX:F2} 上{ph - maxY:F2}"
                });
            }
        }
    }

    private static void CheckSpacing(List<Node> nodes, LayoutAnalyzerOptions options, Page page, LayoutReport report)
    {
        for (int i = 0; i < nodes.Count; i++)
        for (int j = i + 1; j < nodes.Count; j++)
        {
            var a = nodes[i];
            var b = nodes[j];
            double gapX = Math.Abs(a.Bounds.X - b.Bounds.X) - (a.Bounds.W + b.Bounds.W) / 2;
            double gapY = Math.Abs(a.Bounds.Y - b.Bounds.Y) - (a.Bounds.H + b.Bounds.H) / 2;

            // Only report pairs that are neighbours on one axis.
            if (Math.Abs(a.Bounds.Y - b.Bounds.Y) < (a.Bounds.H + b.Bounds.H) / 2 && gapX >= 0 && gapX < options.MinNodeGap)
            {
                report.Add(new LayoutIssue
                {
                    Code = "spacing.too-tight",
                    Severity = "warning",
                    Page = page.Name,
                    Targets = new[] { a.Id, b.Id },
                    Message = $"'{a.Id}' 与 '{b.Id}' 水平间距仅 {gapX:F2}\"（建议 ≥ {options.MinNodeGap:F2}\"）"
                });
            }
            else if (Math.Abs(a.Bounds.X - b.Bounds.X) < (a.Bounds.W + b.Bounds.W) / 2 && gapY >= 0 && gapY < options.MinNodeGap)
            {
                report.Add(new LayoutIssue
                {
                    Code = "spacing.too-tight",
                    Severity = "warning",
                    Page = page.Name,
                    Targets = new[] { a.Id, b.Id },
                    Message = $"'{a.Id}' 与 '{b.Id}' 垂直间距仅 {gapY:F2}\"（建议 ≥ {options.MinNodeGap:F2}\"）"
                });
            }
        }
    }

    // ---- geometry helpers ---------------------------------------------------

    private static bool Overlaps(Bounds a, Bounds b, double tolerance)
    {
        double ax1 = a.X - a.W / 2, ax2 = a.X + a.W / 2, ay1 = a.Y - a.H / 2, ay2 = a.Y + a.H / 2;
        double bx1 = b.X - b.W / 2, bx2 = b.X + b.W / 2, by1 = b.Y - b.H / 2, by2 = b.Y + b.H / 2;
        double ix = Math.Min(ax2, bx2) - Math.Max(ax1, bx1);
        double iy = Math.Min(ay2, by2) - Math.Max(ay1, by1);
        return ix > tolerance && iy > tolerance;
    }

    private static bool Encloses(Bounds outer, Bounds inner, double tolerance)
    {
        double ox1 = outer.X - outer.W / 2, ox2 = outer.X + outer.W / 2;
        double oy1 = outer.Y - outer.H / 2, oy2 = outer.Y + outer.H / 2;
        double ix1 = inner.X - inner.W / 2, ix2 = inner.X + inner.W / 2;
        double iy1 = inner.Y - inner.H / 2, iy2 = inner.Y + inner.H / 2;
        return ix1 >= ox1 - tolerance && ix2 <= ox2 + tolerance
            && iy1 >= oy1 - tolerance && iy2 <= oy2 + tolerance;
    }

    private static bool RectOverlapsRect(Bounds a, Bounds b, double tolerance) => Overlaps(a, b, tolerance);

    /// <summary>Approximates the connector path: explicit waypoints, or a straight/elbow run.</summary>
    /// <summary>
    /// Direct check: does a straight edge between two nodes cross a third? Exposed separately
    /// so callers can validate author-supplied geometry without invoking the router.
    /// </summary>
    public static string? CheckEdgeThroughNode(Node from, Node to, IEnumerable<Node> others, Edge edge)
    {
        var path = edge.Waypoints is { Count: > 0 }
            ? new List<Point> { new(from.Bounds.X, from.Bounds.Y) }
                .Concat(edge.Waypoints)
                .Append(new Point(to.Bounds.X, to.Bounds.Y))
                .ToList()
            : new List<Point>
            {
                BoundaryPoint(from.Bounds, to.Bounds.X - from.Bounds.X, to.Bounds.Y - from.Bounds.Y),
                BoundaryPoint(to.Bounds, from.Bounds.X - to.Bounds.X, from.Bounds.Y - to.Bounds.Y)
            };

        foreach (var node in others)
        {
            if (node.Id == from.Id || node.Id == to.Id) continue;
            if (SegmentIntersectsRect(path, node.Bounds, 0.04)) return node.Id;
        }
        return null;
    }

    private static List<Point> RoutePoints(Node from, Node to, Edge edge)
    {
        // When the router emitted waypoints it already decided the exit/entry sides, and its
        // first/last segments start at those sides' midpoints. Mirror that here so the checker
        // measures the same polyline the renderer draws.
        bool routed = edge.Waypoints is { Count: > 0 };
        Point start, end;
        if (routed)
        {
            // Anchor on the side the first/last segment actually travels toward, so the
            // measured polyline matches the drawn one.
            var first = edge.Waypoints![0];
            var last = edge.Waypoints[^1];
            start = AnchorToward(from.Bounds, first, edge.Waypoints.Count > 1 ? edge.Waypoints[1] : null);
            end = AnchorToward(to.Bounds, last, edge.Waypoints.Count > 1 ? edge.Waypoints[^2] : null);
        }
        else
        {
            start = PortPoint(from, to, edge.FromPort);
            end = PortPoint(to, from, edge.ToPort);
        }

        var points = new List<Point> { start };
        if (routed)
        {
            foreach (var wp in edge.Waypoints!) points.Add(wp);
        }
        else if (edge.Kind is "rightAngle" or "right-angle" or "orthogonal")
        {
            double midX = (start.X + end.X) / 2;
            points.Add(new Point(midX, start.Y));
            points.Add(new Point(midX, end.Y));
        }
        points.Add(end);
        return points;
    }

    /// <summary>
    /// Picks the shape border point for a waypoint, preferring the axis the following segment
    /// runs along so the resulting polyline is orthogonal.
    /// </summary>
    private static Point AnchorToward(Bounds b, Point waypoint, Point? next)
    {
        double dx = waypoint.X - b.X, dy = waypoint.Y - b.Y;
        double halfW = b.W / 2, halfH = b.H / 2;

        // Vertical run after the waypoint: enter/leave through a horizontal side.
        if (next is not null && Math.Abs(next.X - waypoint.X) < 1e-6)
            return new Point(waypoint.X, b.Y + Math.Sign(dy) * halfH);
        // Horizontal run after the waypoint: enter/leave through a vertical side.
        if (next is not null && Math.Abs(next.Y - waypoint.Y) < 1e-6)
            return new Point(b.X + Math.Sign(dx) * halfW, waypoint.Y);

        return NearestSidePoint(b, waypoint);
    }

    /// <summary>
    /// Projects the target point onto the rectangle border following an axis-aligned run.
    /// When the waypoint already sits exactly on a side's centre line the projection is that
    /// point; otherwise it extends the segment perpendicular to the nearest side so the drawn
    /// polyline never cuts a diagonal corner.
    /// </summary>
    private static Point NearestSidePoint(Bounds b, Point target)
    {
        double dx = target.X - b.X, dy = target.Y - b.Y;
        double halfW = b.W / 2, halfH = b.H / 2;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return new Point(b.X, b.Y);

        // Outside the box on one axis: project straight out on that axis.
        if (Math.Abs(dx) >= halfW || Math.Abs(dy) >= halfH)
        {
            if (Math.Abs(dx) - halfW >= Math.Abs(dy) - halfH && Math.Abs(dx) >= halfW)
                return new Point(b.X + Math.Sign(dx) * halfW, target.Y);
            if (Math.Abs(dy) >= halfH)
                return new Point(target.X, b.Y + Math.Sign(dy) * halfH);
        }

        // Compare normalised offsets so the nearer side wins regardless of shape aspect.
        return Math.Abs(dx) / halfW >= Math.Abs(dy) / halfH
            ? new Point(b.X + Math.Sign(dx) * halfW, b.Y)
            : new Point(b.X, b.Y + Math.Sign(dy) * halfH);
    }

    private static bool IsOrthogonalKind(string? kind) =>
        kind is not null && (kind.Equals("rightAngle", StringComparison.OrdinalIgnoreCase)
                          || kind.Equals("right-angle", StringComparison.OrdinalIgnoreCase)
                          || kind.Equals("orthogonal", StringComparison.OrdinalIgnoreCase)
                          || kind.Equals("elbow", StringComparison.OrdinalIgnoreCase));

    /// <summary>Midpoint of the named side of a shape, or the boundary point toward the peer.</summary>
    private static Point PortPoint(Node self, Node peer, string? port)
    {
        var b = self.Bounds;
        return (port ?? "auto").ToLowerInvariant() switch
        {
            "left" => new Point(b.X - b.W / 2, b.Y),
            "right" => new Point(b.X + b.W / 2, b.Y),
            "top" => new Point(b.X, b.Y + b.H / 2),
            "bottom" => new Point(b.X, b.Y - b.H / 2),
            _ => BoundaryPoint(b, peer.Bounds.X - b.X, peer.Bounds.Y - b.Y)
        };
    }

    /// <summary>Point on the source shape's boundary facing the target.</summary>
    private static Point ExitPoint(Node from, Node to)
    {
        double dx = to.Bounds.X - from.Bounds.X;
        double dy = to.Bounds.Y - from.Bounds.Y;
        return BoundaryPoint(from.Bounds, dx, dy);
    }

    private static Point EntryPoint(Node from, Node to)
    {
        double dx = from.Bounds.X - to.Bounds.X;
        double dy = from.Bounds.Y - to.Bounds.Y;
        return BoundaryPoint(to.Bounds, dx, dy);
    }

    /// <summary>
    /// Intersection of the ray from the rectangle centre toward (dx,dy) with the rectangle
    /// border. dx/dy are direction deltas, so a pure horizontal ray is handled by the
    /// infinity guard on the vertical scale.
    /// </summary>
    internal static Point BoundaryPoint(Bounds b, double dx, double dy)
    {
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return new Point(b.X, b.Y);
        double halfW = b.W / 2, halfH = b.H / 2;
        double scaleX = Math.Abs(dx) < 1e-9 ? double.PositiveInfinity : halfW / Math.Abs(dx);
        double scaleY = Math.Abs(dy) < 1e-9 ? double.PositiveInfinity : halfH / Math.Abs(dy);
        double scale = Math.Min(scaleX, scaleY);
        return new Point(b.X + dx * scale, b.Y + dy * scale);
    }

    private static bool SegmentIntersectsRect(List<Point> path, Bounds rect, double clearance)
    {
        double x1 = rect.X - rect.W / 2 - clearance, x2 = rect.X + rect.W / 2 + clearance;
        double y1 = rect.Y - rect.H / 2 - clearance, y2 = rect.Y + rect.H / 2 + clearance;

        for (int i = 0; i < path.Count - 1; i++)
        {
            if (SegmentIntersectsRect(path[i], path[i + 1], x1, y1, x2, y2)) return true;
        }
        return false;
    }

    /// <summary>Liang–Barsky segment/rectangle clipping test.</summary>
    private static bool SegmentIntersectsRect(Point a, Point b, double x1, double y1, double x2, double y2)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double t0 = 0, t1 = 1;

        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-12) return q >= 0;
            double r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }

        if (!Clip(-dx, a.X - x1)) return false;
        if (!Clip(dx, x2 - a.X)) return false;
        if (!Clip(-dy, a.Y - y1)) return false;
        if (!Clip(dy, y2 - a.Y)) return false;
        return true;
    }

    private static Point PointAlong(List<Point> path, double fraction)
    {
        double total = 0;
        for (int i = 0; i < path.Count - 1; i++) total += Distance(path[i], path[i + 1]);
        if (total <= 0) return path[0];

        double target = total * fraction, walked = 0;
        for (int i = 0; i < path.Count - 1; i++)
        {
            double seg = Distance(path[i], path[i + 1]);
            if (walked + seg >= target)
            {
                double t = seg <= 0 ? 0 : (target - walked) / seg;
                return new Point(path[i].X + (path[i + 1].X - path[i].X) * t,
                                 path[i].Y + (path[i + 1].Y - path[i].Y) * t);
            }
            walked += seg;
        }
        return path[^1];
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static bool SharesEndpoint(Edge a, Edge b) =>
        a.From == b.From || a.To == b.To || a.From == b.To || a.To == b.From;

    private static bool PathsCross(List<Point> a, List<Point> b)
    {
        for (int i = 0; i < a.Count - 1; i++)
        for (int j = 0; j < b.Count - 1; j++)
            if (SegmentsIntersect(a[i], a[i + 1], b[j], b[j + 1])) return true;
        return false;
    }

    private static bool SegmentsIntersect(Point p1, Point p2, Point p3, Point p4)
    {
        double d = (p2.X - p1.X) * (p4.Y - p3.Y) - (p2.Y - p1.Y) * (p4.X - p3.X);
        if (Math.Abs(d) < 1e-12) return false;

        double t = ((p3.X - p1.X) * (p4.Y - p3.Y) - (p3.Y - p1.Y) * (p4.X - p3.X)) / d;
        double u = ((p3.X - p1.X) * (p2.Y - p1.Y) - (p3.Y - p1.Y) * (p2.X - p1.X)) / d;

        const double eps = 1e-9;
        return t > eps && t < 1 - eps && u > eps && u < 1 - eps;
    }
}

public sealed class LayoutAnalyzerOptions
{
    public double OverlapTolerance { get; init; } = 0.02;
    /// <summary>Extra distance a connector must keep from an unrelated shape.</summary>
    public double NodeClearance { get; init; } = 0.04;
    public double MinNodeGap { get; init; } = 0.18;
    public double MinPageUtilisation { get; init; } = 0.30;
}

public sealed class LayoutReport
{
    public List<LayoutIssue> Issues { get; } = new();
    public Dictionary<string, double> PageUtilisation { get; } = new();
    public Dictionary<string, Bounds> Bounds { get; } = new();

    public void Add(LayoutIssue issue) => Issues.Add(issue);

    public IEnumerable<LayoutIssue> Errors => Issues.Where(i => i.Severity == "error");
    public IEnumerable<LayoutIssue> Warnings => Issues.Where(i => i.Severity == "warning");
    public IEnumerable<LayoutIssue> Infos => Issues.Where(i => i.Severity == "info");

    public bool IsClean => !Errors.Any() && !Warnings.Any();

    /// <summary>0..100 heuristic score used by the agent iteration loop.</summary>
    public int Score
    {
        get
        {
            int score = 100;
            score -= Errors.Count() * 12;
            score -= Warnings.Count() * 4;
            score -= Infos.Count() * 1;
            return Math.Clamp(score, 0, 100);
        }
    }

    public IDictionary<string, int> CountByCode()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var issue in Issues)
            counts[issue.Code] = counts.TryGetValue(issue.Code, out var n) ? n + 1 : 1;
        return counts;
    }
}

public sealed class LayoutIssue
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "info";
    public string Page { get; set; } = "";
    public string[] Targets { get; set; } = Array.Empty<string>();
    public string Message { get; set; } = "";
}

/// <summary>Crude but conservative text metrics used for overflow detection.</summary>
public static class TextMetrics
{
    private const double DefaultFontSize = 11.0;
    private const double DefaultBoldWidthFactor = 1.06;

    public static TextEstimate Estimate(string text, Node node)
    {
        double fontSize = node.Style?.FontSize ?? DefaultFontSize;
        bool bold = node.Style?.Bold ?? false;
        double widthFactor = bold ? DefaultBoldWidthFactor : 1.0;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        double widest = 0;
        foreach (var line in lines)
            widest = Math.Max(widest, MeasureLine(line, fontSize, widthFactor));

        double lineHeight = fontSize / 72.0 * 1.28;
        double required = lines.Length * lineHeight + 0.10;

        return new TextEstimate(widest, required, lines.Length);
    }

    public static double MeasureLine(string line, double fontSize, double widthFactor = 1.0)
    {
        double widthPt = 0;
        foreach (var ch in line)
        {
            if (ch >= '\u2E80') widthPt += fontSize * 1.0;       // CJK ideographs are square
            else if (char.IsWhiteSpace(ch)) widthPt += fontSize * 0.28;
            else if ("iljtfrI.,:;'!|".Contains(ch)) widthPt += fontSize * 0.30;
            else if ("MWmw@%".Contains(ch)) widthPt += fontSize * 0.86;
            else if (char.IsUpper(ch)) widthPt += fontSize * 0.66;
            else widthPt += fontSize * 0.53;
        }
        return widthPt / 72.0 * widthFactor;
    }
}

public readonly record struct TextEstimate(double LongestLineInches, double RequiredHeightInches, int LineCount);
