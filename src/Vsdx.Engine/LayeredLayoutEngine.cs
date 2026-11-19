using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Stacks nodes into horizontal bands and orders them left to right inside each band.
///
/// Band membership comes from (in priority order): an explicit layer map, the node's container,
/// then an inferred rank from the edge direction. Ordering inside a band follows the average
/// position of each node's predecessors, which keeps the reading path left-to-right instead of
/// scattering connectors.
/// </summary>
public sealed class LayeredLayoutEngine : ILayoutEngine
{
    public LayoutStrategy Strategy => LayoutStrategy.Layered;

    public LayoutResult Layout(Page page, LayoutOptions options)
    {
        var notes = new List<string>();
        if (page.Nodes.Count == 0)
            return new LayoutResult { Strategy = Strategy, Notes = { "页面没有节点" } };

        var bands = AssignBands(page, options, notes);
        var ordered = OrderWithinBands(page, bands);

        // Reorder within each band to reduce crossings. The order above comes from the graph and is stable, but
        // stable is not the same as readable: three nodes above three others with the connectors reversed cross
        // three times, and rotating the lower band removes all three without touching the router.
        //
        // Measured on that case before this existed: 94 with three `edge.crossing`, 98 with none after reversing
        // the lower band by hand.
        MinimiseCrossings(page, ordered);

        const double baseY = 2.0;
        double y = baseY;
        double maxContentWidth = 0;
        int bandCount = 0;

        // Layer numbers grow upward (see AssignBands), and the page cursor starts at the
        // bottom, so bands are drawn in ascending order: layer 0 first at the lowest Y.
        foreach (var band in ordered.OrderBy(b => b.Key))
        {
            var members = band.Value;
            if (members.Count == 0) continue;

            bandCount++;
            double rowHeight = members.Max(n => n.Bounds.H);
            double rowWidth = members.Sum(n => n.Bounds.W) + options.NodeGapX * (members.Count - 1);

            // With EvenBands every row is placed inside the same horizontal envelope, so the bands stack flush
            // instead of each ending where its own members happen to stop. Rows with fewer or narrower members are
            // centred in that envelope rather than left-aligned into it — a short row hugging the left edge reads
            // as an omission, while a centred one reads as deliberate.
            double x = options.PageMargin + 0.30;
            double slack = options.EvenBands ? Math.Max(0, (maxContentWidth - rowWidth) / 2) : 0;
            x += slack;

            // Containers sharing a layer stand side by side, and their boxes extend past their members by the
            // container padding on each side. Spacing consecutive members by `NodeGapX` alone therefore lets the
            // boxes overlap: measured, two containers meeting at x=2.60/2.65 with a 0.05" overlap. A member that
            // starts a new container gets the padding back, plus a gutter.
            // Members of one container stay together: the row is ordered container by container, so a cell's nodes
            // do not interleave with its neighbour's. Without this the whole layer was one continuous run and each
            // container's box stretched across every other container — measured, three Slack cells each 10.78"
            // wide while their members covered about 4".
            var byContainer = members
                .GroupBy(n => n.GroupId ?? "")
                .OrderBy(grp => grp.Min(n => n.Bounds.X))
                .SelectMany(grp => grp)
                .ToList();

            string? previousContainer = null;

            foreach (var node in byContainer)
            {
                if (previousContainer is not null && !string.Equals(previousContainer, node.GroupId, StringComparison.Ordinal))
                    x += 2 * DiagramLayout.ContainerPaddingX + options.BandGapY * 0.6;

                node.Bounds.X = Round(x + node.Bounds.W / 2);
                node.Bounds.Y = Round(y + rowHeight / 2);
                x += node.Bounds.W + options.NodeGapX;

                previousContainer = node.GroupId;
            }

            maxContentWidth = Math.Max(maxContentWidth, rowWidth);

            // Container bands are drawn around their members: they extend above the row by the
            // heading height and below by the bottom padding (see DiagramLayout). Advancing by
            // the row height alone makes adjacent containers overlap by exactly that padding,
            // so reserve the full band envelope plus the requested gutter.
            double containerEnvelope = DiagramLayout.ContainerHeading + DiagramLayout.HeadingClearance
                                       + DiagramLayout.ContainerPaddingBottom;
            y += rowHeight + containerEnvelope + options.BandGapY;
        }

        // Recompute containers from their members, then report the content box.
        DiagramLayout.Apply(new Diagram { Pages = { page } });

        // Bands are derived from their members, so by now each has its own width. Widening them all to the widest
        // makes the layer stack flush; the members stay where they are, centred in the new envelope.
        if (options.EvenBands) MakeEvenBands(page);

        double width = maxContentWidth + options.PageMargin * 2 + 0.60;
        double height = y + options.PageMargin;

        page.Size ??= new PageSize();
        page.Size.Width = Round(Math.Max(page.Size.Width, width));
        page.Size.Height = Round(Math.Max(page.Size.Height, height));

        return new LayoutResult
        {
            Strategy = Strategy,
            Layers = bandCount,
            NodesPlaced = page.Nodes.Count,
            ContentWidth = maxContentWidth,
            ContentHeight = height,
            Notes = { }
        }
        .WithNotes(notes);
    }

    /// <summary>
    /// Groups nodes into bands. Explicit layer map wins, then container membership, then a rank
    /// derived from the longest path through the edge graph.
    /// </summary>
    /// <summary>
    /// Gives every container on the page the same width and the same left and right edges.
    ///
    /// A band is sized from its own members, so a row of circles and a row of rectangles produce different widths
    /// and the stack ends in a staircase. This widens the narrower ones; nothing is narrowed, so no member can be
    /// left outside its container.
    /// </summary>
    private static void MakeEvenBands(Page page)
    {
        var sized = page.Groups.Where(g => g.Bounds is not null).ToList();
        if (sized.Count < 2) return;

        // Evenness applies per row, not across the page: containers sharing a layer stand side by side and must
        // keep their own columns, while containers on different rows still align to one envelope. Flattening every
        // container to the same x and width erased the side-by-side arrangement entirely — measured on the Slack
        // case, three cells landed at x[0.55,17.25] on top of each other.
        // The envelope every single-container row aligns to is measured across the whole page, not per row: a
        // layer that stands alone should still be flush with the layers above and below it. Measured on `cbo`,
        // banding per row alone gave 6.04 / 5.60 / 5.60 instead of three equal widths.
        double pageLeft = sized.Min(g => g.Bounds!.X - g.Bounds!.W / 2);
        double pageRight = sized.Max(g => g.Bounds!.X + g.Bounds!.W / 2);

        foreach (var row in sized.GroupBy(g => Math.Round(g.Bounds!.Y, 2)))
        {
            var rowGroups = row.ToList();
            if (rowGroups.Count == 0) continue;

            // A row of one container stretches to the page envelope. A row of several keeps each container's own
            // column — flattening them would land every cell in the same place.
            bool single = rowGroups.Count == 1;

            foreach (var group in rowGroups)
            {
                var b = group.Bounds!;

                if (single)
                    group.Bounds = new Bounds((pageLeft + pageRight) / 2, b.Y, pageRight - pageLeft, b.H);
                else
                    group.Bounds = new Bounds(b.X, b.Y, b.W, b.H);

                // Pin it: `DiagramLayout.Apply` re-derives width from members, which would undo the alignment on
                // the next pass — `execute` runs one after the layout engines have finished.
                group.AutoSize = false;
            }
        }
    }

    private static Dictionary<int, List<Node>> AssignBands(
        Page page, LayoutOptions options, List<string> notes)
    {
        var bands = new Dictionary<int, List<Node>>();
        var nodes = page.Nodes;
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        // 1. Explicit layer assignment from the caller.
        if (options.LayerOfNode is not null)
        {
            foreach (var node in nodes)
            {
                int layer = options.LayerOfNode.TryGetValue(node.Id, out var l) ? l : 0;
                Add(bands, layer, node);
            }
            notes.Add("分层由显式 layer 映射决定");
            return bands;
        }

        // 2. Container membership: each container becomes one band, ordered by its Y position so
        //    the author's intent about which band is on top is preserved.
        // Order containers by the mean Y of their members, not by the container's own cached
        // bounds: those bounds may be stale or auto-derived and do not reliably express which
        // band the author meant to sit on top.
        var containers = page.Groups
            .Where(g => g.Members.Count > 0)
            .Select(g => new
            {
                Group = g,
                MeanY = g.Members
                    .Select(id => byId.TryGetValue(id, out var n) ? n.Bounds.Y : double.NaN)
                    .Where(y => !double.IsNaN(y))
                    .DefaultIfEmpty(g.Bounds?.Y ?? 0)
                    .Average()
            })
            .OrderByDescending(x => x.MeanY)
            .Select(x => x.Group)
            .ToList();

        if (containers.Count > 0)
        {
            // Containers that share an `order` share a layer, which is what makes them sit side by side instead of
            // stacking. Every container used to get its own layer number unconditionally, so an architecture whose
            // defining shape is parallel units — a cellular topology, a set of regional replicas — came out as a
            // list. Measured before this: two containers with the same order rendered at (1.50, 3.42) and
            // (1.50, 1.23).
            //
            // Containers without an order keep the old behaviour of one layer each, so existing diagrams that
            // never set `order` are unaffected.
            var ordered = containers
                .OrderByDescending(g => g.Order ?? int.MinValue)
                .ThenByDescending(g => g.Members
                    .Select(id => byId.TryGetValue(id, out var n) ? n.Bounds.Y : double.NaN)
                    .Where(y => !double.IsNaN(y))
                    .DefaultIfEmpty(g.Bounds?.Y ?? 0)
                    .Average())
                .ToList();

            // Group adjacent containers that declare the same order into one layer.
            int layer = ordered.Count;
            int i = 0;

            while (i < ordered.Count)
            {
                var head = ordered[i];
                int runEnd = i;

                if (head.Order is int sameOrder)
                {
                    while (runEnd + 1 < ordered.Count && ordered[runEnd + 1].Order == sameOrder) runEnd++;
                }

                for (int k = i; k <= runEnd; k++)
                {
                    foreach (var memberId in ordered[k].Members)
                    {
                        if (byId.TryGetValue(memberId, out var node)) Add(bands, layer, node);
                    }
                }

                layer--;
                i = runEnd + 1;
            }

            // Nodes outside any container go in a band of their own, above every container.
            //
            // The layer number has to exceed the highest one used, not be derived from where the loop finished.
            // `layer + 2` looked right and was not: with two container groups the loop ends at 0, so loose nodes
            // landed on layer 2 — the same layer as the topmost container. Measured on `slack-cellular`, a client
            // with no `containerId` was drawn inside the "边缘层（跨区）" band, telling the reader it lives there.
            var placed = bands.Values.SelectMany(x => x).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            int aboveAll = bands.Count == 0 ? 1 : bands.Keys.Max() + 1;

            foreach (var node in nodes.Where(n => !placed.Contains(n.Id)))
                Add(bands, aboveAll, node);

            notes.Add($"分层由 {containers.Count} 个容器决定（同 order 的容器共享一层，可并排）");
            return bands;
        }

        // 3. Infer layers from the edge graph (longest-path ranking).
        //
        // Rank() measures distance upstream, so a source scores 0 and a sink scores highest.
        // Page Y grows upward and higher layer numbers are drawn higher, so the layer must be
        // inverted: the source (nothing feeds it) belongs at the top of the diagram.
        int maxRank = nodes.Count == 0 ? 0 : nodes.Max(n => Rank(byId, page.Edges, n.Id));
        foreach (var node in nodes)
        {
            int upstream = Rank(byId, page.Edges, node.Id);
            Add(bands, maxRank - upstream, node);
        }

        notes.Add("分层由连线方向推断（上游节点在上）");
        return bands;
    }

    /// <summary>Longest-path rank; cycles are broken by treating a revisited node as rank 0.</summary>
    private static int Rank(
        Dictionary<string, Node> byId, IEnumerable<Edge> edges, string nodeId)
    {
        var incoming = edges
            .Where(e => e.To == nodeId && e.From is not null && byId.ContainsKey(e.From))
            .Select(e => e.From!)
            .ToList();

        if (incoming.Count == 0) return 0;

        // Depth-limited to avoid runaway on cyclic graphs; 64 levels is far beyond any real chart.
        int best = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        var stack = new Stack<(string id, int depth)>();
        foreach (var src in incoming) stack.Push((src, 1));

        while (stack.Count > 0)
        {
            var (id, depth) = stack.Pop();
            if (depth > 64) break;
            best = Math.Max(best, depth);
            if (!visited.Add(id)) continue;

            foreach (var e in edges.Where(e => e.To == id && e.From is not null))
            {
                if (e.From is null) continue;
                stack.Push((e.From, depth + 1));
            }
        }
        return best;
    }

    /// <summary>
    /// Orders nodes inside a band by the mean X of their predecessors, so a node appears after
    /// the things that feed it. Falls back to the author's X when there are no predecessors.
    /// </summary>
    /// <summary>
    /// Reorders nodes inside each band to reduce connector crossings, by adjacent swaps.
    ///
    /// Only the position within a band changes — never which band a node belongs to, and never the node set. That
    /// keeps the operation safe: the report it is optimising is computed on the same graph, and a band that had
    /// one node still has one.
    ///
    /// Adjacent swapping is enough here because the bands are single rows. A full Sugiyama pass would pair it
    /// with a barycentre initialisation and a transpose phase; this is the transpose phase alone, which is where
    /// the crossings in these diagrams come from.
    /// </summary>
    private static void MinimiseCrossings(Page page, Dictionary<int, List<Node>> bands)
    {
        if (bands.Count < 2) return;

        var bandOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (layer, members) in bands)
            foreach (var node in members)
                bandOf[node.Id] = layer;

        // Edges that span two different bands are the ones whose crossings depend on ordering. Edges inside one
        // band cannot cross each other through reordering, and are left out.
        var spanning = page.Edges
            .Where(e => e.From is not null && e.To is not null
                     && bandOf.ContainsKey(e.From) && bandOf.ContainsKey(e.To)
                     && bandOf[e.From] != bandOf[e.To])
            .ToList();

        if (spanning.Count == 0) return;

        int before = CountCrossings(bands, spanning);
        if (before == 0) return;

        // Repeatedly sweep each band and accept a swap when it lowers the count. Bounded so a pathological graph
        // cannot spin: each pass either improves the count or ends the loop.
        for (int pass = 0; pass < 8; pass++)
        {
            bool improved = false;

            foreach (var layer in bands.Keys.OrderBy(k => k))
            {
                var members = bands[layer];

                for (int i = 0; i + 1 < members.Count; i++)
                {
                    (members[i], members[i + 1]) = (members[i + 1], members[i]);

                    int after = CountCrossings(bands, spanning);

                    if (after < before)
                    {
                        before = after;
                        improved = true;
                    }
                    else
                    {
                        (members[i], members[i + 1]) = (members[i + 1], members[i]);
                    }
                }
            }

            if (!improved || before == 0) break;
        }
    }

    /// <summary>
    /// Counts pairs of spanning edges that cross, using each node's index within its band.
    ///
    /// Two edges cross when their endpoints sit in the same two bands and the order of their endpoints is
    /// inverted. Edges sharing an endpoint never count: they meet, they do not cross.
    /// </summary>
    private static int CountCrossings(
        Dictionary<int, List<Node>> bands, List<Vsdx.Model.Edge> spanning)
    {
        var position = new Dictionary<string, (int Layer, int Index)>(StringComparer.Ordinal);

        foreach (var (layer, members) in bands)
            for (int i = 0; i < members.Count; i++)
                position[members[i].Id] = (layer, i);

        var placed = spanning
            .Where(e => position.ContainsKey(e.From!) && position.ContainsKey(e.To!))
            .Select(e => (From: position[e.From!], To: position[e.To!], Id: e.Id))
            .ToList();

        int crossings = 0;

        for (int a = 0; a < placed.Count; a++)
        for (int b = a + 1; b < placed.Count; b++)
        {
            var (ea, eb) = (placed[a], placed[b]);

            // Only edges between the same pair of bands can cross each other.
            if (ea.From.Layer != eb.From.Layer || ea.To.Layer != eb.To.Layer) continue;

            // Sharing an endpoint is a meeting, not a crossing.
            if (ea.Id == eb.Id) continue;
            if (position[spanning[a].From!] == position[spanning[b].From!]) continue;
            if (position[spanning[a].To!] == position[spanning[b].To!]) continue;

            // Inverted endpoint order.
            bool inverted = (ea.From.Index - eb.From.Index) * (ea.To.Index - eb.To.Index) < 0;
            if (inverted) crossings++;
        }

        return crossings;
    }

    private static Dictionary<int, List<Node>> OrderWithinBands(
        Page page, Dictionary<int, List<Node>> bands)
    {
        var byId = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        var result = new Dictionary<int, List<Node>>();
        foreach (var (layer, members) in bands)
        {
            // Order by graph position, not by predecessor X.
            //
            // The previous implementation scored a node by the mean X of its predecessors and
            // gave sources double.MaxValue. That inverted every simple chain: a source has no
            // predecessors, so it scored highest and was placed *after* its own successor.
            // It also leaked absolute coordinates into ordering, so relayout results depended on
            // the author's original X values.
            //
            // Correct ordering: walk the graph within this band. A node is emitted once all of
            // its in-band predecessors have been emitted; ties break on author X for stability.
            var membersInBand = members.ToHashSet();
            var inBandPreds = members.ToDictionary(
                n => n,
                n => page.Edges
                    .Where(e => e.To == n.Id && e.From is not null
                             && byId.ContainsKey(e.From) && membersInBand.Contains(byId[e.From!]))
                    .Select(e => byId[e.From!])
                    .Distinct()
                    .ToList());

            var emitted = new HashSet<Node>();
            var orderedMembers = new List<Node>();
            var pending = members
                .OrderBy(n => n.Bounds.X)
                .ThenBy(n => n.Id, StringComparer.Ordinal)
                .ToList();

            while (pending.Count > 0)
            {
                // Emit every node whose in-band predecessors are already placed.
                var ready = pending.Where(n => inBandPreds[n].All(emitted.Contains)).ToList();

                // A cycle leaves nothing ready; break it deterministically rather than stalling.
                if (ready.Count == 0) ready = new List<Node> { pending[0] };

                foreach (var node in ready)
                {
                    orderedMembers.Add(node);
                    emitted.Add(node);
                    pending.Remove(node);
                }
            }

            var scored = orderedMembers;

            result[layer] = scored;
        }
        return result;
    }

    private static void Add(Dictionary<int, List<Node>> bands, int layer, Node node)
    {
        if (!bands.TryGetValue(layer, out var list))
            bands[layer] = list = new List<Node>();
        if (!list.Contains(node)) list.Add(node);
    }

    private static double Round(double v) => Math.Round(v * 40) / 40.0;
}

/// <summary>Uniform rows and columns; predictable and compact for inventories.</summary>
public sealed class GridLayoutEngine : ILayoutEngine
{
    public LayoutStrategy Strategy => LayoutStrategy.Grid;

    public LayoutResult Layout(Page page, LayoutOptions options)
    {
        if (page.Nodes.Count == 0)
            return new LayoutResult { Strategy = Strategy, Notes = { "页面没有节点" } };

        // A grid scatters container members across unrelated cells, which then makes the
        // containers themselves overlap. When the page is grouped, a banded layout is the only
        // shape that keeps the author's grouping intact.
        if (page.Groups.Any(g => g.Members.Count > 0))
        {
            var fallback = new LayeredLayoutEngine().Layout(page, options);
            return new LayoutResult
            {
                Strategy = Strategy,
                Layers = fallback.Layers,
                NodesPlaced = fallback.NodesPlaced,
                ContentWidth = fallback.ContentWidth,
                ContentHeight = fallback.ContentHeight,
                Notes = { "页面含容器，grid 退化为 layered 以保持分组" }
            };
        }

        int perRow = Math.Max(1, options.MaxNodesPerRow);
        double cellW = page.Nodes.Max(n => n.Bounds.W);
        double cellH = page.Nodes.Max(n => n.Bounds.H);

        double x = options.PageMargin + cellW / 2;
        double y = options.PageMargin + cellH / 2;
        int col = 0, rows = 0;

        // Fill bottom-up so the first node lands top-left after rendering.
        int rowsTotal = (int)Math.Ceiling(page.Nodes.Count / (double)perRow);
        y = options.PageMargin + cellH / 2 + (rowsTotal - 1) * (cellH + options.BandGapY);

        foreach (var node in page.Nodes)
        {
            node.Bounds.X = Round(x);
            node.Bounds.Y = Round(y);

            col++;
            if (col >= perRow)
            {
                col = 0;
                rows++;
                x = options.PageMargin + cellW / 2;
                y -= cellH + options.BandGapY;
            }
            else
            {
                x += cellW + options.NodeGapX;
            }
        }

        DiagramLayout.Apply(new Diagram { Pages = { page } });

        double width = perRow * cellW + (perRow - 1) * options.NodeGapX + options.PageMargin * 2;
        double height = rowsTotal * cellH + (rowsTotal - 1) * options.BandGapY + options.PageMargin * 2;

        page.Size ??= new PageSize();
        page.Size.Width = Round(Math.Max(page.Size.Width, width));
        page.Size.Height = Round(Math.Max(page.Size.Height, height));

        return new LayoutResult
        {
            Strategy = Strategy,
            Layers = rowsTotal,
            NodesPlaced = page.Nodes.Count,
            ContentWidth = width,
            ContentHeight = height
        };
    }

    private static double Round(double v) => Math.Round(v * 40) / 40.0;
}

/// <summary>Keeps author-supplied coordinates untouched.</summary>
public sealed class ManualLayoutEngine : ILayoutEngine
{
    public LayoutStrategy Strategy => LayoutStrategy.Manual;

    public LayoutResult Layout(Page page, LayoutOptions options)
    {
        DiagramLayout.Apply(new Diagram { Pages = { page } });

        var xs = page.Nodes.Select(n => n.Bounds.X).ToList();
        var ys = page.Nodes.Select(n => n.Bounds.Y).ToList();

        return new LayoutResult
        {
            Strategy = Strategy,
            NodesPlaced = page.Nodes.Count,
            ContentWidth = xs.Count > 0 ? xs.Max() - xs.Min() : 0,
            ContentHeight = ys.Count > 0 ? ys.Max() - ys.Min() : 0,
            Notes = { "沿用模型中的坐标" }
        };
    }
}

internal static class LayoutResultExtensions
{
    public static LayoutResult WithNotes(this LayoutResult result, List<string> notes)
    {
        foreach (var note in notes) result.Notes.Add(note);
        return result;
    }
}
