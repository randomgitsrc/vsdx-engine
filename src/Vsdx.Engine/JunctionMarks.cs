using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Marks the points where a bus splits.
///
/// When several connectors share a trunk and turn off at their own target's x, nothing on the page says they belong
/// to one wire. A reader sees lines that happen to start near each other. Worse, the opposite reading is available:
/// a line ending near another line looks like a line joining it, which is how the fourth review saw two API
/// instances joined to each other.
///
/// A junction dot is the conventional answer and its absence is equally meaningful — no dot means the lines merely
/// cross.
///
/// The marks are nodes rather than path decorations because the writer has no way to put a dot on a connector, and
/// a small `Ellipse` node is something every downstream reader already handles.
/// </summary>
public static class JunctionMarks
{
    /// <summary>Diameter in inches. Small enough to read as a mark, large enough to survive at print size.</summary>
    public const double Diameter = 0.10;

    /// <summary>
    /// Adds a mark at every point where three or more routed connectors share a trunk and turn away from it.
    /// Returns how many were added.
    /// </summary>
    public static int Apply(Diagram diagram)
    {
        int added = 0;

        foreach (var page in diagram.Pages)
        {
            var routed = page.Edges.Where(e => e.Waypoints is { Count: >= 2 }).ToList();
            if (routed.Count < 2) continue;

            // A trunk is a horizontal or vertical run that more than one connector travels. Group the runs by their
            // position and direction, then keep the groups with at least two members.
            var runs = new Dictionary<(bool Horizontal, double At), List<(Edge Edge, Point Point)>>();

            foreach (var edge in routed)
            {
                for (int i = 0; i < edge.Waypoints!.Count - 1; i++)
                {
                    var a = edge.Waypoints[i];
                    var b = edge.Waypoints[i + 1];

                    bool horizontal = Math.Abs(a.Y - b.Y) < 0.01;
                    bool vertical = Math.Abs(a.X - b.X) < 0.01;
                    if (!horizontal && !vertical) continue;

                    // The point where this connector leaves the run is its far end: that is where it turns away.
                    double at = Math.Round(horizontal ? a.Y : a.X, 2);
                    var key = (horizontal, at);

                    if (!runs.TryGetValue(key, out var list)) runs[key] = list = new();
                    list.Add((edge, b));
                }
            }

            var marks = new List<Point>();

            foreach (var run in runs.Values.Where(r => r.Count >= 2))
            {
                // The split is where the members stop sharing the trunk — the far end of the run.
                foreach (var (_, turn) in run)
                    marks.Add(turn);
            }

            // One dot per distinct location: two branches turning at the same place are one junction.
            var distinct = marks
                .Select(p => (X: Math.Round(p.X, 2), Y: Math.Round(p.Y, 2)))
                .Distinct()
                .ToList();

            foreach (var (x, y) in distinct)
            {
                // Never stack a dot on a node: it would read as part of the shape.
                bool onNode = page.Nodes.Any(n =>
                    Math.Abs(n.Bounds.X - x) < n.Bounds.W / 2 && Math.Abs(n.Bounds.Y - y) < n.Bounds.H / 2);

                if (onNode) continue;

                page.Nodes.Add(new Node
                {
                    Id = $"junction-{page.Id}-{added}",
                    Text = "",
                    Shape = "Ellipse",
                    Bounds = new Bounds(x, y, Diameter, Diameter),
                    Style = new ShapeStyle
                    {
                        Fill = "#37474F",
                        Stroke = "#37474F",
                        StrokeWidth = 0
                    }
                });

                added++;
            }
        }

        return added;
    }
}
