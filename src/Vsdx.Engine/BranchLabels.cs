using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Places a bus branch's label on its own descent instead of at the midpoint of the whole route.
///
/// A label sits at a fraction along its route. When three branches share a trunk, that midpoint lands on the shared
/// part, so the labels cluster together and each one drifts toward a neighbour's target. Measured on
/// `deploy-topology`: the three `9000` labels sat at x = 3.91 / 5.02 / 5.64 while their targets were at 1.50 / 3.35 /
/// 5.20 — off by up to 2.41 inches, all in the same direction. The numbers are in the wrong order relative to what
/// they label, which is worse than being slightly off: a reader can follow a label to the wrong instance.
///
/// The fix is geometric, not cosmetic: measure where the branch leaves the trunk, and place the label below it on
/// the vertical run that enters the target.
/// </summary>
public static class BranchLabels
{
    /// <summary>Fraction of the descent at which the label sits, measured from the trunk.</summary>
    public const double DescentFraction = 0.55;

    /// <summary>
    /// Re-places labels on routed edges that share a trunk with another edge. Returns how many were moved.
    /// </summary>
    public static int Apply(Diagram diagram)
    {
        int moved = 0;

        foreach (var page in diagram.Pages)
        {
            var routed = page.Edges.Where(e => e.Waypoints is { Count: >= 2 }).ToList();
            if (routed.Count < 2) continue;

            // Which connectors share a horizontal run with another one? Those are the ones whose midpoint is on a
            // trunk rather than on their own branch.
            var trunkRows = routed
                .SelectMany(e => HorizontalRuns(e))
                .GroupBy(r => Math.Round(r.Y, 2))
                .Where(g => g.Count() >= 2)
                .Select(g => g.Key)
                .ToHashSet();

            if (trunkRows.Count == 0) continue;

            var byId = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

            foreach (var edge in routed)
            {
                if (edge.LabelPosition is not null) continue;              // the author placed it
                if (string.IsNullOrWhiteSpace(edge.Label)) continue;
                if (!byId.TryGetValue(edge.From ?? "", out var from)) continue;
                if (!byId.TryGetValue(edge.To ?? "", out var to)) continue;

                // Only edges whose route sits on a shared trunk.
                bool onTrunk = HorizontalRuns(edge).Any(r => trunkRows.Contains(Math.Round(r.Y, 2)));
                if (!onTrunk) continue;

                var fraction = FractionOfDescent(from.Bounds, to.Bounds, edge.Waypoints!, trunkRows, to.Bounds.Y);
                if (fraction is null) continue;

                edge.LabelPosition = fraction;
                moved++;
            }
        }

        return moved;
    }

    private static IEnumerable<(double Y, double X0, double X1)> HorizontalRuns(Edge edge)
    {
        if (edge.Waypoints is not { Count: >= 2 }) yield break;

        for (int i = 0; i < edge.Waypoints.Count - 1; i++)
        {
            var a = edge.Waypoints[i];
            var b = edge.Waypoints[i + 1];
            if (Math.Abs(a.Y - b.Y) < 0.01)
                yield return (a.Y, Math.Min(a.X, b.X), Math.Max(a.X, b.X));
        }
    }

    /// <summary>
    /// The arclength fraction at which the route has descended most of the way to the target — that is the vertical
    /// run entering it, where a reader looks to see which instance a label belongs to.
    /// </summary>
    private static double? FractionOfDescent(
        Bounds from, Bounds to, List<Point> waypoints, HashSet<double> trunkRows, double targetY)
    {
        var pts = new List<Point> { new(from.X, from.Y) };
        pts.AddRange(waypoints);
        pts.Add(new Point(to.X, to.Y));

        double total = 0;
        for (int i = 0; i < pts.Count - 1; i++) total += Dist(pts[i], pts[i + 1]);
        if (total < 1e-6) return null;

        // Walk to the last point that is still on the trunk; the descent starts there.
        double walked = 0;
        double? trunkEnd = null;

        for (int i = 0; i < pts.Count - 1; i++)
        {
            double seg = Dist(pts[i], pts[i + 1]);
            bool horizontal = Math.Abs(pts[i].Y - pts[i + 1].Y) < 0.01;

            if (horizontal && trunkRows.Contains(Math.Round(pts[i].Y, 2)))
                trunkEnd = walked + seg;

            walked += seg;
        }

        if (trunkEnd is not double start) return null;

        // Half way down the remaining vertical run, so the label clears the trunk and the arrowhead.
        double descent = total - start;
        if (descent < 0.2) return null;

        return Math.Clamp((start + descent * DescentFraction) / total, 0.05, 0.95);
    }

    private static double Dist(Point a, Point b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
