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
}
