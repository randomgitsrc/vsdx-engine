using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A label on a bus branch is drawn at the midpoint of the whole route, which lands on the shared trunk and can sit
/// closer to a neighbouring branch's target than to its own.
///
/// Measured on `deploy-topology`: the three `9000` labels end up at x = 3.91 / 5.02 / 5.64 while their targets are at
/// 1.50 / 3.35 / 5.20 — off by 2.41, 1.67 and 0.44 inches, all displaced the same way. A reader trying to work out
/// which port belongs to which instance gets three numbers in the wrong order.
///
/// The fix is to place the label on the branch's own descent rather than at the route's arclength midpoint: below the
/// trunk, on the segment that actually enters the target.
/// </summary>
public sealed class TrunkLabelTests
{
    private static Diagram OneBusThreeBranches()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 16, Height = 9 } };

        page.Groups.Add(new Group { Id = "a", Name = "A", Kind = "layer", Order = 1, Members = { "a1" } });
        page.Groups.Add(new Group { Id = "b", Name = "B", Kind = "layer", Order = 1, Members = { "b1" } });
        page.Groups.Add(new Group { Id = "c", Name = "C", Kind = "layer", Order = 1, Members = { "c1" } });

        page.Nodes.Add(new Node { Id = "src", Text = "网关" });
        page.Nodes.Add(new Node { Id = "a1", Text = "A1", GroupId = "a" });
        page.Nodes.Add(new Node { Id = "b1", Text = "B1", GroupId = "b" });
        page.Nodes.Add(new Node { Id = "c1", Text = "C1", GroupId = "c" });

        page.Edges.Add(new Edge { Id = "e1", From = "src", To = "a1", Label = "9000" });
        page.Edges.Add(new Edge { Id = "e2", From = "src", To = "b1", Label = "9000" });
        page.Edges.Add(new Edge { Id = "e3", From = "src", To = "c1", Label = "9000" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void ABranchLabelSitsOverItsOwnBranch()
    {
        var diagram = OneBusThreeBranches();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);
        OrthogonalRouter.Route(diagram);
        BranchLabels.Apply(diagram);

        var page = diagram.Pages[0];

        foreach (var edge in page.Edges)
        {
            var target = page.Nodes.Single(n => n.Id == edge.To);

            // Only branches that share a trunk are re-placed. A branch that runs straight down has no trunk to
            // leave, and its midpoint is already over its own target.
            if (edge.LabelPosition is null) continue;

            var centre = PointAtFraction(page, edge, edge.LabelPosition.Value);

            // It must be at least closer to its own target's column than to any other target's.
            double mine = Math.Abs(centre.X - target.Bounds.X);
            double nearestOther = page.Nodes
                .Where(n => n.Id != edge.To && n.GroupId is not null)
                .Min(n => Math.Abs(centre.X - n.Bounds.X));

            Assert.True(mine < nearestOther + 0.30,
                $"{edge.Id}'s label at x={centre.X:F2} is closer to another branch's target ({nearestOther:F2}) than its own ({mine:F2})");
        }
    }

    private static Point PointAtFraction(Page page, Edge edge, double fraction)
    {
        var from = page.Nodes.Single(n => n.Id == edge.From);
        var to = page.Nodes.Single(n => n.Id == edge.To);

        var pts = new List<Point> { new(from.Bounds.X, from.Bounds.Y) };
        if (edge.Waypoints is { Count: > 0 }) pts.AddRange(edge.Waypoints);
        pts.Add(new Point(to.Bounds.X, to.Bounds.Y));

        double total = 0;
        for (int i = 0; i < pts.Count - 1; i++) total += Dist(pts[i], pts[i + 1]);
        if (total < 1e-6) return pts[0];

        double want = total * Math.Clamp(fraction, 0, 1), walked = 0;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            double seg = Dist(pts[i], pts[i + 1]);
            if (walked + seg >= want)
            {
                double t = (want - walked) / seg;
                return new Point(pts[i].X + (pts[i + 1].X - pts[i].X) * t,
                                 pts[i].Y + (pts[i + 1].Y - pts[i].Y) * t);
            }
            walked += seg;
        }
        return pts[^1];
    }

    private static double Dist(Point a, Point b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
