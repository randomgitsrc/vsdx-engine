using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Swimlane layout: horizontal bands, each with its own left-to-right flow, plus a lane header.
///
/// Phase 7 gap G4: `Group.Kind == "lane"` was accepted by the model but read by nothing, so the
/// fourth of the ten required diagram categories (cross-functional swimlane) could not be
/// produced at all. Lanes are not a new geometry algorithm — they are containers that stack
/// vertically while their contents flow horizontally, which is why this reuses the banded
/// machinery rather than inventing a second layout system.
/// </summary>
public sealed class SwimlaneLayoutEngine : ILayoutEngine
{
    public LayoutStrategy Strategy => LayoutStrategy.Layered;

    public LayoutResult Layout(Page page, LayoutOptions options)
    {
        var lanes = page.Groups
            .Where(g => string.Equals(g.Kind, "lane", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.Bounds?.Y ?? 0)
            .ToList();

        if (lanes.Count == 0)
        {
            // No lanes declared: fall back to the ordinary banded layout rather than producing
            // an empty page.
            return new LayeredLayoutEngine().Layout(page, options);
        }

        var notes = new List<string> { $"泳道布局：{lanes.Count} 条泳道" };
        var byId = page.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        // Loose nodes (not in any lane) go in a band above the lanes.
        var inLanes = lanes.SelectMany(l => l.Members).ToHashSet(StringComparer.Ordinal);
        var loose = page.Nodes.Where(n => !inLanes.Contains(n.Id)).ToList();

        double y = options.PageMargin;
        double maxWidth = 0;
        int bandCount = 0;

        // Draw bottom-up: the last lane in the list sits lowest.
        var orderedLanes = lanes.AsEnumerable().Reverse().ToList();
        // Loose nodes belong ABOVE the lanes, but the cursor starts at the bottom, so they are
        // placed after the lanes have claimed their rows.
        if (loose.Count > 0)
            notes.Add($"{loose.Count} 个节点不属于任何泳道，置于顶部独立一行");

        foreach (var lane in orderedLanes)
        {
            var members = lane.Members
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();

            if (members.Count == 0) continue;

            var row = OrderAlongEdges(page, members);
            var (width, height) = PlaceRow(row, y, options);
            maxWidth = Math.Max(maxWidth, width);

            // Reserve the lane header plus breathing room.
            double envelope = DiagramLayout.ContainerHeading + DiagramLayout.ContainerPaddingBottom;
            y += height + envelope + options.BandGapY;
            bandCount++;
        }

        if (loose.Count > 0)
        {
            var row = OrderAlongEdges(page, loose);
            var (width, height) = PlaceRow(row, y, options);
            maxWidth = Math.Max(maxWidth, width);
            y += height + options.BandGapY;
            bandCount++;
        }

        DiagramLayout.Apply(new Diagram { Pages = { page } });

        page.Size ??= new PageSize();
        page.Size.Width = Round(Math.Max(page.Size.Width, maxWidth + options.PageMargin * 2 + 0.60));
        page.Size.Height = Round(Math.Max(page.Size.Height, y + options.PageMargin));

        return new LayoutResult
        {
            Strategy = Strategy,
            Layers = bandCount,
            NodesPlaced = page.Nodes.Count,
            ContentWidth = maxWidth,
            ContentHeight = y,
            Notes = { }
        }.WithNotes(notes);
    }

    /// <summary>Places one row starting at <paramref name="y"/>, returning its width and height.</summary>
    private static (double width, double height) PlaceRow(List<Node> row, double y, LayoutOptions options)
    {
        if (row.Count == 0) return (0, 0);

        double rowHeight = row.Max(n => n.Bounds.H);
        double x = options.PageMargin + 0.30;
        double startX = x;

        foreach (var node in row)
        {
            node.Bounds.X = Round(x + node.Bounds.W / 2);
            node.Bounds.Y = Round(y + rowHeight / 2);
            x += node.Bounds.W + options.NodeGapX;
        }

        return (x - startX, rowHeight);
    }

    /// <summary>
    /// Orders a lane's nodes left to right along their edges, so the flow reads in sequence
    /// rather than following whatever order the author happened to list them in.
    /// </summary>
    private static List<Node> OrderAlongEdges(Page page, List<Node> members)
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

            // A cycle leaves nothing ready; emit the leftmost deterministically rather than stall.
            if (ready.Count == 0) ready = new List<Node> { pending[0] };

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
}
