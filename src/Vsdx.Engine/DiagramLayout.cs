using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Deterministic geometry helpers. Group containers must fully enclose their members or the
/// rendered preview shows nodes spilling outside their band, so container bounds are derived
/// from member geometry rather than trusted from hand-written JSON.
/// </summary>
public static class DiagramLayout
{
    /// <summary>Horizontal padding between a container border and its members.</summary>
    public const double ContainerPaddingX = 0.30;

    /// <summary>Space reserved above members for the container heading.</summary>
    public const double ContainerHeading = 0.46;

    /// <summary>Space below members before the container border.</summary>
    public const double ContainerPaddingBottom = 0.22;

    /// <summary>
    /// Recomputes container bounds from member node geometry. Groups are processed
    /// outer-most first so nested groups inherit corrected child bounds.
    /// </summary>
    public static void Apply(Diagram diagram)
    {
        foreach (var page in diagram.Pages)
        {
            var nodes = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

            foreach (var group in OrderGroups(page))
            {
                if (group.Bounds is not null && group.AutoSize == false) continue;

                var members = group.Members
                    .Where(nodes.ContainsKey)
                    .Select(id => nodes[id])
                    .ToList();

                var childGroups = page.Groups
                    .Where(g => g.ParentId == group.Id && g.Bounds is not null)
                    .Select(g => g.Bounds!)
                    .ToList();

                if (members.Count == 0 && childGroups.Count == 0) continue;

                double left = double.PositiveInfinity, right = double.NegativeInfinity;
                double bottom = double.PositiveInfinity, top = double.NegativeInfinity;

                foreach (var node in members)
                {
                    left = Math.Min(left, node.Bounds.X - node.Bounds.W / 2);
                    right = Math.Max(right, node.Bounds.X + node.Bounds.W / 2);
                    bottom = Math.Min(bottom, node.Bounds.Y - node.Bounds.H / 2);
                    top = Math.Max(top, node.Bounds.Y + node.Bounds.H / 2);
                }

                foreach (var bounds in childGroups)
                {
                    left = Math.Min(left, bounds.X - bounds.W / 2);
                    right = Math.Max(right, bounds.X + bounds.W / 2);
                    bottom = Math.Min(bottom, bounds.Y - bounds.H / 2);
                    top = Math.Max(top, bounds.Y + bounds.H / 2);
                }

                double padX = group.Style?.PaddingX ?? ContainerPaddingX;
                double heading = group.Style?.HeadingHeight ?? ContainerHeading;
                double padBottom = group.Style?.PaddingBottom ?? ContainerPaddingBottom;

                left -= padX;
                right += padX;
                bottom -= padBottom;
                top += heading;

                group.Bounds = new Bounds((left + right) / 2, (bottom + top) / 2, right - left, top - bottom);
            }
        }
    }


    /// <summary>
    /// Reflects every page across the line y = x, turning a wide layout into a tall one.
    ///
    /// This is a reflection, not a rotation, which matters for two details:
    ///   * a shape rotated by θ becomes one rotated by 90 − θ, not by θ or −θ;
    ///   * two applications are the identity, because 90 − (90 − θ) = θ.
    ///
    /// Everything positioned in page coordinates must be mirrored together. Transposing the
    /// node boxes but leaving their labels, the free paths, or the container padding behind
    /// produces a diagram whose parts disagree about which way is up — a failure that looks
    /// like a styling bug but is really a geometry bug.
    /// </summary>
    public static void Transpose(Diagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);

        foreach (var page in diagram.Pages)
        {
            foreach (var node in page.Nodes)
            {
                SwapBounds(node.Bounds);
                node.Angle = TransposeAngle(node.Angle);
            }

            foreach (var group in page.Groups)
            {
                if (group.Bounds is not null) SwapBounds(group.Bounds);

                // Container padding is expressed along named axes, so it has to trade sides too.
                if (group.Style is { } style)
                {
                    (style.PaddingX, style.HeadingHeight) = (style.HeadingHeight, style.PaddingX);
                }
            }

            // Text blocks move with the sheet but keep their proportions. Swapping width and height
            // turned a 4.00 x 0.60 title into 0.60 x 4.00, and the only way to set a horizontal
            // title in a 0.60" column is to stack the characters — the heading ran top to bottom
            // while the diagram ran left to right.
            foreach (var text in page.TextBlocks)
            {
                (text.Bounds.X, text.Bounds.Y) = (text.Bounds.Y, text.Bounds.X);
            }

            foreach (var path in page.Paths)
            {
                SwapBounds(path.Bounds);
                path.Angle = TransposeAngle(path.Angle);
                TransposeGeometry(path);
            }

            foreach (var edge in page.Edges)
            {
                SwapPoint(edge.FromPoint);
                SwapPoint(edge.ToPoint);

                if (edge.Waypoints is { } points)
                {
                    foreach (var point in points) SwapPoint(point);
                }
            }

            if (page.Size is not null)
            {
                (page.Size.Width, page.Size.Height) = (page.Size.Height, page.Size.Width);
            }
        }

        // The top-level page size is a second copy of the same fact; leaving it alone makes the
        // document disagree with its only page.
        if (diagram.PageSize is { } pageSize)
        {
            (pageSize.Width, pageSize.Height) = (pageSize.Height, pageSize.Width);
        }

        diagram.Orientation = diagram.Orientation?.ToLowerInvariant() switch
        {
            "portrait" => "landscape",
            "landscape" => "portrait",
            var other => other
        };
    }


    /// <summary>Reflection across y = x maps an angle θ to 90° − θ.</summary>
    private static double? TransposeAngle(double? angle) =>
        angle is { } value ? NormalizeAngle(90.0 - value) : null;

    private static double NormalizeAngle(double degrees)
    {
        double normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static void SwapBounds(Bounds bounds) =>
        (bounds.X, bounds.Y, bounds.W, bounds.H) = (bounds.Y, bounds.X, bounds.H, bounds.W);

    private static void SwapPoint(Point? point)
    {
        if (point is null) return;
        (point.X, point.Y) = (point.Y, point.X);
    }

    /// <summary>
    /// Mirrors the raw geometry rows. Row cells are string-keyed (X, Y, A, B, C, D, ...) and are
    /// written verbatim into the package, so they must be swapped in place; drop them and the
    /// outline would no longer match the box it was fitted to.
    /// </summary>
    private static void TransposeGeometry(FreePath path)
    {
        foreach (var geometry in path.Geometry)
        {
            foreach (var row in geometry.Rows) TransposeRowCells(row.Cells);
        }
    }

    private static void TransposeRowCells(Dictionary<string, string> cells)
    {
        SwapCell(cells, "X", "Y");
        SwapCell(cells, "A", "B");
        SwapCell(cells, "C", "D");
    }

    private static void SwapCell(Dictionary<string, string> cells, string first, string second)
    {
        bool hasFirst = cells.TryGetValue(first, out var a);
        bool hasSecond = cells.TryGetValue(second, out var b);

        if (hasFirst && hasSecond) { cells[first] = b!; cells[second] = a!; }
        else if (hasFirst) { cells.Remove(first); cells[second] = a!; }
        else if (hasSecond) { cells.Remove(second); cells[first] = b!; }
    }

    /// <summary>Groups ordered so that parents are laid out before their children.</summary>
    private static IEnumerable<Group> OrderGroups(Page page)
    {
        var byId = page.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var ordered = new List<Group>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        void Visit(Group group)
        {
            if (ordered.Contains(group)) return;
            if (!visiting.Add(group.Id)) return; // cycle guard
            if (group.ParentId is not null && byId.TryGetValue(group.ParentId, out var parent)) Visit(parent);
            ordered.Add(group);
            visiting.Remove(group.Id);
        }

        foreach (var group in page.Groups) Visit(group);
        return ordered;
    }

    /// <summary>
    /// Checks every group encloses its members, returning human-readable problems.
    /// Used by validation and by the Agent iteration loop.
    /// </summary>
    public static List<string> CheckContainment(Page page, double tolerance = 0.02)
    {
        var problems = new List<string>();
        var nodes = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        foreach (var group in page.Groups)
        {
            if (group.Bounds is null) continue;
            var g = group.Bounds;
            double gLeft = g.X - g.W / 2, gRight = g.X + g.W / 2;
            double gBottom = g.Y - g.H / 2, gTop = g.Y + g.H / 2;

            foreach (var memberId in group.Members)
            {
                if (!nodes.TryGetValue(memberId, out var node)) continue;
                var b = node.Bounds;
                double nLeft = b.X - b.W / 2, nRight = b.X + b.W / 2;
                double nBottom = b.Y - b.H / 2, nTop = b.Y + b.H / 2;

                if (nLeft < gLeft - tolerance || nRight > gRight + tolerance ||
                    nBottom < gBottom - tolerance || nTop > gTop + tolerance)
                {
                    problems.Add(
                        $"node '{memberId}' escapes group '{group.Id}' " +
                        $"[node {nLeft:F2},{nBottom:F2}..{nRight:F2},{nTop:F2} vs group {gLeft:F2},{gBottom:F2}..{gRight:F2},{gTop:F2}]");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Reflects every page about its vertical centre, so left and right trade places.
    ///
    /// Needed because the layered engine stacks bands upward and transposing turns "up" into "left":
    /// measured, a chain declared web -> sdk -> ... -> bi came out with web at x=21.82 and bi at
    /// x=2.27, i.e. reading right-to-left.
    ///
    /// Kept out of <see cref="Transpose(Page)"/> on purpose. Swapping is its own inverse; composing
    /// it with a reflection is not — `(x,y) -> (y, W-x)` applied twice gives `(W-x, W-y)`, which is
    /// a half turn. Callers rely on transposing twice restoring the page, so the reflection is a
    /// separate step the pipeline asks for when it wants a left-to-right result.
    /// </summary>
    public static void MirrorHorizontally(Diagram diagram)
    {
        foreach (var page in diagram.Pages)
        {
            double width = page.Size?.Width ?? 0;
            if (width <= 0) continue;

            double Flip(double x) => width - x;

            foreach (var node in page.Nodes) node.Bounds.X = Flip(node.Bounds.X);

            foreach (var group in page.Groups)
            {
                if (group.Bounds is not null) group.Bounds.X = Flip(group.Bounds.X);
            }

            foreach (var text in page.TextBlocks) text.Bounds.X = Flip(text.Bounds.X);
            foreach (var path in page.Paths) path.Bounds.X = Flip(path.Bounds.X);

            foreach (var edge in page.Edges)
            {
                if (edge.FromPoint is not null) edge.FromPoint.X = Flip(edge.FromPoint.X);
                if (edge.ToPoint is not null) edge.ToPoint.X = Flip(edge.ToPoint.X);

                if (edge.Waypoints is null) continue;
                foreach (var point in edge.Waypoints)
                {
                    if (point is null) continue;
                    point.X = Flip(point.X);
                }
            }
        }
    }
}
