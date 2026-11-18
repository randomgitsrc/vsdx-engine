using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Re-lays out a single container while leaving everything else byte-identical.
///
/// Phase 7 gap G5: the repair loop could diagnose "this region needs relayout" but the only
/// available action was a whole-page relayout, which moves unrelated objects — the opposite of
/// what brief §6.9 asks for ("protect unrelated objects").
///
/// The strategy here is deliberately narrow: only the members of the target container move, and
/// the container keeps its anchor (its centre stays put) so the surrounding page is undisturbed.
/// </summary>
public static class LocalRelayout
{
    /// <summary>
    /// Re-lays the members of <paramref name="groupId"/> inside their current container box.
    /// Returns a report describing what moved.
    /// </summary>
    public static LocalRelayoutResult Apply(
        Diagram diagram, string groupId, LayoutStrategy strategy = LayoutStrategy.Layered,
        LayoutOptions? options = null)
    {
        options ??= new LayoutOptions();
        var moved = new List<string>();

        foreach (var page in diagram.Pages)
        {
            var group = page.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group is null) continue;

            var members = page.Nodes
                .Where(n => string.Equals(n.GroupId, groupId, StringComparison.Ordinal))
                .ToList();

            if (members.Count == 0)
            {
                return new LocalRelayoutResult
                {
                    GroupId = groupId,
                    Moved = moved,
                    Note = "容器内没有节点"
                };
            }

            // Remember the container's anchor so it can be restored after the members move.
            var anchor = group.Bounds is null
                ? (Bounds?)null
                : new Bounds(group.Bounds.X, group.Bounds.Y, group.Bounds.W, group.Bounds.H);

            // Snapshot every node outside the target so we can prove nothing else moved.
            var untouched = page.Nodes
                .Where(n => !string.Equals(n.GroupId, groupId, StringComparison.Ordinal))
                .ToDictionary(
                    n => n.Id,
                    n => (n.Bounds.X, n.Bounds.Y, n.Bounds.W, n.Bounds.H),
                    StringComparer.Ordinal);

            ArrangeMembers(members, page, strategy, options);

            // Restore the container anchor and grow it only if the new arrangement needs more room.
            if (anchor is not null)
            {
                DiagramLayout.Apply(new Diagram { Pages = { page } });

                var resized = page.Groups.First(g => g.Id == groupId).Bounds;
                if (resized is not null)
                {
                    // Keep the anchor; expand around it if members now need more space.
                    page.Groups.First(g => g.Id == groupId).Bounds = new Bounds(
                        anchor.X,
                        anchor.Y,
                        Math.Max(anchor.W, resized.W),
                        Math.Max(anchor.H, resized.H));
                }
            }

            foreach (var member in members) moved.Add(member.Id);

            // Verify the isolation guarantee before reporting success.
            foreach (var (id, before) in untouched)
            {
                var after = page.Nodes.First(n => n.Id == id).Bounds;
                if (Math.Abs(after.X - before.X) > 1e-6 ||
                    Math.Abs(after.Y - before.Y) > 1e-6 ||
                    Math.Abs(after.W - before.W) > 1e-6 ||
                    Math.Abs(after.H - before.H) > 1e-6)
                {
                    return new LocalRelayoutResult
                    {
                        GroupId = groupId,
                        Moved = moved,
                        Note = $"隔离性被破坏：容器外节点 '{id}' 被移动",
                        IsolationBroken = true
                    };
                }
            }

            // Isolation says the other shapes were left alone; it says nothing about whether the
            // container's new layout landed on top of them. Checked separately, because reporting
            // isolation as success is how "container back relaid as grid" accompanied two overlapping
            // pairs on overview.ir.json — web over api by 1.71", mobile over worker by 1.15".
            var collisions = new List<string>();
            var memberIds = members.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var member in members)
            {
                foreach (var other in page.Nodes)
                {
                    if (memberIds.Contains(other.Id)) continue;
                    if (!Overlaps(member.Bounds, other.Bounds)) continue;

                    collisions.Add(
                        $"'{member.Id}' overlaps '{other.Id}' " +
                        $"[{member.Bounds.W:F2}x{member.Bounds.H:F2} at " +
                        $"({member.Bounds.X:F2},{member.Bounds.Y:F2})]");
                }
            }

            return new LocalRelayoutResult
            {
                GroupId = groupId,
                Moved = moved,
                Note = collisions.Count == 0
                    ? $"重排了容器 '{groupId}' 内的 {members.Count} 个节点"
                    : $"重排了容器 '{groupId}' 内的 {members.Count} 个节点，但与容器外 {collisions.Count} 个节点重叠",
                IsolationBroken = false,
                Collisions = collisions
            };
        }

        return new LocalRelayoutResult
        {
            GroupId = groupId,
            Moved = moved,
            Note = $"找不到容器 '{groupId}'"
        };
    }

    /// <summary>
    /// Arranges the container's members in a single row (layered), a flow along their edges, or a
    /// grid, anchored at the container's current left/bottom so the region does not drift.
    /// </summary>
    private static void ArrangeMembers(
        List<Node> members, Page page, LayoutStrategy strategy, LayoutOptions options)
    {
        double left = members.Min(n => n.Bounds.X - n.Bounds.W / 2);
        double bottom = members.Min(n => n.Bounds.Y - n.Bounds.H / 2);

        var ordered = OrderAlongEdges(members, page);

        if (strategy == LayoutStrategy.Grid)
        {
            int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(ordered.Count)));
            double cellW = ordered.Max(n => n.Bounds.W) + options.NodeGapX;
            double cellH = ordered.Max(n => n.Bounds.H) + options.RowGapY;

            for (int i = 0; i < ordered.Count; i++)
            {
                int row = i / columns;
                int col = i % columns;
                ordered[i].Bounds.X = Round(left + col * cellW + ordered[i].Bounds.W / 2);
                ordered[i].Bounds.Y = Round(bottom + (ordered.Count / columns - row) * cellH
                                            + ordered[i].Bounds.H / 2);
            }
            return;
        }

        // Layered and Flow both read left to right inside one band.
        double x = left;
        double rowHeight = ordered.Max(n => n.Bounds.H);
        foreach (var node in ordered)
        {
            node.Bounds.X = Round(x + node.Bounds.W / 2);
            node.Bounds.Y = Round(bottom + rowHeight / 2);
            x += node.Bounds.W + options.NodeGapX;
        }
    }

    /// <summary>Topological order restricted to the given members, author X as tiebreak.</summary>
    private static List<Node> OrderAlongEdges(List<Node> members, Page page)
    {
        var ids = members.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var byId = members.ToDictionary(m => m.Id, StringComparer.Ordinal);

        var preds = members.ToDictionary(
            m => m,
            m => page.Edges
                .Where(e => e.To == m.Id && e.From is not null && ids.Contains(e.From))
                .Select(e => byId[e.From!])
                .Distinct()
                .ToList());

        var emitted = new HashSet<Node>();
        var result = new List<Node>();
        var pending = members.OrderBy(n => n.Bounds.X).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();

        while (pending.Count > 0)
        {
            var ready = pending.Where(n => preds[n].All(emitted.Contains)).ToList();
            if (ready.Count == 0) ready = new List<Node> { pending[0] };   // cycle guard

            foreach (var node in ready)
            {
                result.Add(node);
                emitted.Add(node);
                pending.Remove(node);
            }
        }
        return result;
    }

    private static double Round(double v) => Math.Round(v * 40) / 40.0;

    private static bool Overlaps(Bounds a, Bounds b)
    {
        double ox = Math.Min(a.X + a.W / 2, b.X + b.W / 2) - Math.Max(a.X - a.W / 2, b.X - b.W / 2);
        double oy = Math.Min(a.Y + a.H / 2, b.Y + b.H / 2) - Math.Max(a.Y - a.H / 2, b.Y - b.H / 2);

        return ox > 0.05 && oy > 0.05;
    }
}

public sealed class LocalRelayoutResult
{
    public required string GroupId { get; init; }
    public required IReadOnlyList<string> Moved { get; init; }
    public string? Note { get; init; }

    /// <summary>True when a node outside the container moved, violating the isolation guarantee.</summary>
    public bool IsolationBroken { get; init; }

    /// <summary>
    /// Members of the relayed container that now overlap a node outside it.
    ///
    /// Distinct from <see cref="IsolationBroken"/>: isolation says the other shapes were not moved,
    /// this says the result does not land on them. Both must hold for the relayout to be usable.
    /// </summary>
    public IReadOnlyList<string> Collisions { get; init; } = Array.Empty<string>();

    public bool Ok => !IsolationBroken && Collisions.Count == 0 && Moved.Count > 0;
}
