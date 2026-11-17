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

            double x = options.PageMargin + 0.30;
            foreach (var node in members)
            {
                node.Bounds.X = Round(x + node.Bounds.W / 2);
                node.Bounds.Y = Round(y + rowHeight / 2);
                x += node.Bounds.W + options.NodeGapX;
            }

            maxContentWidth = Math.Max(maxContentWidth, rowWidth);

            // Container bands are drawn around their members: they extend above the row by the
            // heading height and below by the bottom padding (see DiagramLayout). Advancing by
            // the row height alone makes adjacent containers overlap by exactly that padding,
            // so reserve the full band envelope plus the requested gutter.
            double containerEnvelope = DiagramLayout.ContainerHeading + DiagramLayout.ContainerPaddingBottom;
            y += rowHeight + containerEnvelope + options.BandGapY;
        }

        // Recompute containers from their members, then report the content box.
        DiagramLayout.Apply(new Diagram { Pages = { page } });

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
            // containers is ordered top-first (descending Y). Layer numbers grow upward with the
            // page, so the topmost container gets the highest number and ends up drawn highest.
            int topLayer = containers.Count;
            int layer = topLayer;
            foreach (var group in containers)
            {
                foreach (var memberId in group.Members)
                {
                    if (byId.TryGetValue(memberId, out var node)) Add(bands, layer, node);
                }
                layer--;
            }

            // Nodes outside any container go in the band above the topmost container.
            var placed = bands.Values.SelectMany(x => x).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var node in nodes.Where(n => !placed.Contains(n.Id)))
                Add(bands, topLayer + 1, node);

            notes.Add($"分层由 {containers.Count} 个容器决定（按容器 Y 位置自顶向下）");
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
    private static Dictionary<int, List<Node>> OrderWithinBands(
        Page page, Dictionary<int, List<Node>> bands)
    {
        var byId = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        var result = new Dictionary<int, List<Node>>();
        foreach (var (layer, members) in bands)
        {
            var scored = members.Select(node =>
            {
                var preds = page.Edges
                    .Where(e => e.To == node.Id && e.From is not null && byId.ContainsKey(e.From))
                    .Select(e => byId[e.From!].Bounds.X)
                    .ToList();

                double score = preds.Count > 0 ? preds.Average() : double.MaxValue;
                return (node, score);
            })
            .OrderBy(t => t.score)
            .ThenBy(t => t.node.Bounds.X)
            .ThenBy(t => t.node.Id, StringComparer.Ordinal)
            .Select(t => t.node)
            .ToList();

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
