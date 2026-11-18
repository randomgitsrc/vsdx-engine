using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Lays a pipeline out strictly left to right along the edge direction, wrapping onto a new row
/// when the chain would exceed the usable width.
///
/// This is the right shape for business flows: the reader follows arrows, so the order must
/// come from the graph, not from the author's hand-placed X coordinates.
/// </summary>
public sealed class FlowLayoutEngine : ILayoutEngine
{
    public LayoutStrategy Strategy => LayoutStrategy.Flow;

    public LayoutResult Layout(Page page, LayoutOptions options)
    {
        if (page.Nodes.Count == 0)
            return new LayoutResult { Strategy = Strategy, Notes = { "页面没有节点" } };

        // When the page has containers, each band is a separate row: flowing every node in one
        // long chain would spread a container's members across the whole page and make the
        // bands overlap each other.
        if (page.Groups.Any(g => g.Members.Count > 0))
            return LayoutBanded(page, options);

        var sequence = BuildSequence(page, out var notes);

        const double baseY = 2.0;
        double x = options.PageMargin + 0.30;
        double y = baseY;
        double rowHeight = 0;
        double maxRowWidth = 0;
        double rowStartX = x;
        int rows = 1;

        // Estimate the usable width so rows wrap before running off the page.
        double usable = page.Size is { Width: > 0 } size
            ? size.Width - options.PageMargin * 2 - 0.60
            : 14.0;

        foreach (var node in sequence)
        {
            if (x > rowStartX && (x + node.Bounds.W - rowStartX) > usable)
            {
                // Wrap: start a new row below.
                maxRowWidth = Math.Max(maxRowWidth, x - rowStartX);
                y += rowHeight + options.RowGapY;
                x = options.PageMargin + 0.30;
                rowStartX = x;
                rowHeight = 0;
                rows++;
            }

            node.Bounds.X = Round(x + node.Bounds.W / 2);
            node.Bounds.Y = Round(y + node.Bounds.H / 2);

            x += node.Bounds.W + options.NodeGapX;
            rowHeight = Math.Max(rowHeight, node.Bounds.H);
            maxRowWidth = Math.Max(maxRowWidth, x - rowStartX);
        }

        DiagramLayout.Apply(new Diagram { Pages = { page } });

        double width = maxRowWidth + options.PageMargin * 2;
        double height = y + rowHeight + options.PageMargin * 2;

        page.Size ??= new PageSize();
        page.Size.Width = Round(Math.Max(page.Size.Width, width));
        page.Size.Height = Round(Math.Max(page.Size.Height, height));

        return new LayoutResult
        {
            Strategy = Strategy,
            Layers = rows,
            NodesPlaced = page.Nodes.Count,
            ContentWidth = maxRowWidth,
            ContentHeight = height
        }.WithNotes(notes);
    }

    /// <summary>
    /// Lays each container out as its own left-to-right row, stacked bottom-up, then flows the
    /// ungrouped nodes in the band above. Keeps bands intact and the reading path horizontal.
    /// </summary>
    private LayoutResult LayoutBanded(Page page, LayoutOptions options)
    {
        var notes = new List<string>();
        var byId = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        // Descending, because rows are stacked upward from the page margin below. Taking bands in
        // ascending Y put the bottom band first and drew the process bottom-up: a workflow
        // declared intake -> review -> fulfil rendered with fulfilment at the top. LayeredLayoutEngine
        // already ordered this way; the two engines disagreeing meant the same containers came out
        // either way round depending on whether the diagram happened to contain a decision node.
        // Declared order wins; geometry is only a fallback. Container bounds are null before the
        // sizing pass runs, so ordering purely by Bounds.Y gives every band the same key and
        // silently degrades to insertion order — which is how a workflow declared
        // intake -> review -> fulfil came out reversed.
        //
        // Ascending, because rows are stacked upward from the page margin below: the first row
        // ends up lowest, so the band that should read first is laid out last.
        //
        // Declared order wins; geometry is only a fallback. Container bounds are null before the
        // sizing pass, so keying on Bounds.Y gives every band the same value and silently
        // degrades to insertion order — which is how a workflow declared intake -> review ->
        // fulfil rendered with fulfilment at the top.
        var containers = page.Groups
            .Where(g => g.Members.Count > 0)
            .OrderBy(g => g.Order ?? int.MinValue)
            .ThenByDescending(g => g.Bounds?.Y ?? 0)
            .ToList();

        var grouped = containers.SelectMany(g => g.Members).ToHashSet(StringComparer.Ordinal);
        var loose = page.Nodes.Where(n => !grouped.Contains(n.Id)).ToList();

        // Order inside each band by the graph, then by author X.
        var rows = new List<List<Node>>();
        foreach (var container in containers)
        {
            var members = container.Members
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
            if (members.Count > 0) rows.Add(OrderRow(page, members));
        }
        if (loose.Count > 0) rows.Add(OrderRow(page, loose));

        double y = options.PageMargin;
        double maxWidth = 0;

        foreach (var row in rows)
        {
            double rowHeight = row.Max(n => n.Bounds.H);
            double rowWidth = row.Sum(n => n.Bounds.W) + options.NodeGapX * Math.Max(0, row.Count - 1);

            double x = options.PageMargin + 0.30;
            foreach (var node in row)
            {
                node.Bounds.X = Round(x + node.Bounds.W / 2);
                node.Bounds.Y = Round(y + rowHeight / 2);
                x += node.Bounds.W + options.NodeGapX;
            }

            maxWidth = Math.Max(maxWidth, rowWidth);
            double envelope = DiagramLayout.ContainerHeading + DiagramLayout.ContainerPaddingBottom;
            y += rowHeight + envelope + options.BandGapY;
        }

        DiagramLayout.Apply(new Diagram { Pages = { page } });

        page.Size ??= new PageSize();
        page.Size.Width = Round(Math.Max(page.Size.Width, maxWidth + options.PageMargin * 2 + 0.60));
        page.Size.Height = Round(Math.Max(page.Size.Height, y + options.PageMargin));

        return new LayoutResult
        {
            Strategy = Strategy,
            Layers = rows.Count,
            NodesPlaced = page.Nodes.Count,
            ContentWidth = maxWidth,
            ContentHeight = y
        }.WithNotes(notes);
    }

    /// <summary>Orders one row along the edge direction, falling back to author X.</summary>
    private static List<Node> OrderRow(Page page, List<Node> members)
    {
        var ids = members.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var incoming = page.Edges
            .Where(e => e.To is not null && e.From is not null
                     && ids.Contains(e.To!) && ids.Contains(e.From!))
            .GroupBy(e => e.To!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().From!, StringComparer.Ordinal);

        var sorted = new List<Node>();
        var remaining = new List<Node>(members);
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        // Repeatedly take nodes whose in-band predecessor is already emitted.
        bool progress = true;
        while (remaining.Count > 0 && progress)
        {
            progress = false;
            foreach (var node in remaining.ToList())
            {
                bool ready = !incoming.TryGetValue(node.Id, out var pred) || emitted.Contains(pred);
                if (!ready) continue;
                sorted.Add(node);
                emitted.Add(node.Id);
                remaining.Remove(node);
                progress = true;
            }
        }

        sorted.AddRange(remaining.OrderBy(n => n.Bounds.X).ThenBy(n => n.Id, StringComparer.Ordinal));
        return sorted;
    }

    /// <summary>
    /// Produces a linear order by walking the graph from its sources. Disconnected nodes and
    /// cycles are appended in author order so nothing is dropped.
    /// </summary>
    private static List<Node> BuildSequence(Page page, out List<string> notes)
    {
        notes = new List<string>();
        var byId = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var outgoing = page.Edges
            .Where(e => e.From is not null && e.To is not null
                     && byId.ContainsKey(e.From!) && byId.ContainsKey(e.To!))
            .GroupBy(e => e.From!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To!).ToList(), StringComparer.Ordinal);

        var indegree = byId.Keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        foreach (var (from, targets) in outgoing)
            foreach (var to in targets)
                indegree[to] = indegree.GetValueOrDefault(to) + 1;

        // Kahn's algorithm from the sources; ties broken by author X so stable output.
        var ready = indegree.Where(kv => kv.Value == 0)
            .Select(kv => byId[kv.Key])
            .OrderBy(n => n.Bounds.X)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

        var sequence = new List<Node>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        while (ready.Count > 0)
        {
            var node = ready[0];
            ready.RemoveAt(0);
            if (!emitted.Add(node.Id)) continue;
            sequence.Add(node);

            if (!outgoing.TryGetValue(node.Id, out var targets)) continue;
            foreach (var targetId in targets)
            {
                indegree[targetId]--;
                if (indegree[targetId] == 0 && !emitted.Contains(targetId))
                {
                    var target = byId[targetId];
                    if (!ready.Contains(target)) ready.Add(target);
                    ready = ready.OrderBy(n => n.Bounds.X).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();
                }
            }
        }

        // Nodes left over sit on a cycle; append them in author order rather than losing them.
        var leftover = page.Nodes.Where(n => !emitted.Contains(n.Id))
            .OrderBy(n => n.Bounds.X).ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();
        if (leftover.Count > 0)
        {
            notes.Add($"{leftover.Count} 个节点存在环路或无可达路径，按作者顺序追加");
            sequence.AddRange(leftover);
        }

        return sequence;
    }

    private static double Round(double v) => Math.Round(v * 40) / 40.0;
}

/// <summary>
/// Arranges a hierarchy from its root downward, widening at each level. Suits organisation
/// charts and any single-parent tree.
/// </summary>
public sealed class TreeLayoutEngine : ILayoutEngine
{
    public LayoutStrategy Strategy => LayoutStrategy.Tree;

    public LayoutResult Layout(Page page, LayoutOptions options)
    {
        if (page.Nodes.Count == 0)
            return new LayoutResult { Strategy = Strategy, Notes = { "页面没有节点" } };

        // A tree that ignores containers pulls members out of their bands and makes the
        // containers overlap. Fall back to the banded flow, which keeps grouping intact.
        if (page.Groups.Any(g => g.Members.Count > 0))
        {
            var fallback = new FlowLayoutEngine().Layout(page, options);
            return new LayoutResult
            {
                Strategy = Strategy,
                Layers = fallback.Layers,
                NodesPlaced = fallback.NodesPlaced,
                ContentWidth = fallback.ContentWidth,
                ContentHeight = fallback.ContentHeight,
                Notes = { "页面含容器，tree 退化为 flow 以保持分组" }
            };
        }

        var byId = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var children = page.Edges
            .Where(e => e.From is not null && e.To is not null
                     && byId.ContainsKey(e.From!) && byId.ContainsKey(e.To!))
            .GroupBy(e => e.From!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To!).Distinct().ToList(), StringComparer.Ordinal);

        var hasParent = page.Edges
            .Where(e => e.To is not null && byId.ContainsKey(e.To!))
            .Select(e => e.To!)
            .ToHashSet(StringComparer.Ordinal);

        // Roots: nodes nothing points at. If every node has a parent the graph is cyclic, so
        // pick the leftmost node as the root instead of returning nothing.
        var roots = page.Nodes.Where(n => !hasParent.Contains(n.Id))
            .OrderBy(n => n.Bounds.X).ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();
        if (roots.Count == 0) roots.Add(page.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal).First());

        // BFS levels.
        var levels = new List<List<Node>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new List<Node>(roots);
        while (frontier.Count > 0)
        {
            var level = frontier.Where(n => seen.Add(n.Id)).ToList();
            if (level.Count == 0) break;
            levels.Add(level);

            var next = new List<Node>();
            foreach (var node in level)
            {
                if (!children.TryGetValue(node.Id, out var childIds)) continue;
                foreach (var id in childIds)
                    if (!seen.Contains(id)) next.Add(byId[id]);
            }
            frontier = next;
        }

        // Any unreachable node forms its own level rather than vanishing.
        var orphans = page.Nodes.Where(n => !seen.Contains(n.Id)).ToList();
        if (orphans.Count > 0) levels.Add(orphans);

        // Lay out top-down with each level centred on the widest level's span.
        double maxWidth = levels.Select(l => l.Sum(n => n.Bounds.W) + options.NodeGapX * Math.Max(0, l.Count - 1))
            .DefaultIfEmpty(0).Max();

        // levels[0] is the root. The page cursor starts at the bottom and grows upward, so the
        // root must be drawn last to end up on top.
        double y = options.PageMargin;
        int levelCount = 0;
        foreach (var level in levels.AsEnumerable().Reverse())
        {
            double rowWidth = level.Sum(n => n.Bounds.W) + options.NodeGapX * Math.Max(0, level.Count - 1);
            double x = options.PageMargin + (maxWidth - rowWidth) / 2;
            double rowHeight = level.Max(n => n.Bounds.H);

            foreach (var node in level)
            {
                node.Bounds.X = Round(x + node.Bounds.W / 2);
                node.Bounds.Y = Round(y + rowHeight / 2);
                x += node.Bounds.W + options.NodeGapX;
            }

            y += rowHeight + options.BandGapY;
            levelCount++;
        }

        DiagramLayout.Apply(new Diagram { Pages = { page } });

        page.Size ??= new PageSize();
        page.Size.Width = Round(Math.Max(page.Size.Width, maxWidth + options.PageMargin * 2));
        page.Size.Height = Round(Math.Max(page.Size.Height, y + options.PageMargin));

        return new LayoutResult
        {
            Strategy = Strategy,
            Layers = levelCount,
            NodesPlaced = page.Nodes.Count,
            ContentWidth = maxWidth,
            ContentHeight = y
        };
    }

    private static double Round(double v) => Math.Round(v * 40) / 40.0;
}
