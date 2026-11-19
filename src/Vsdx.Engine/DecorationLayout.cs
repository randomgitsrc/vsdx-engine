using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Keeps decorative text clear of container borders.
///
/// Containers are derived from their members, so a band grows after the author placed the title. The
/// existing clamp only checked the page boundary, so a band could expand into a title and nothing
/// moved — the heading then sat across the border, colliding with the band's own label.
///
/// The rule here is deliberately narrow:
///
///   * only text that *straddles* a border moves, never text wholly inside a band;
///   * it moves by the smallest amount that clears, preserving intent;
///   * free paths are untouched, since a rule or divider is drawn relative to what it annotates.
///
/// Moving is the right answer for a page title but not for a zone label, and the difference is
/// visible in the geometry: a title that overlaps the border is broken, one that sits inside is a
/// deliberate placement.
/// </summary>
public static class DecorationLayout
{
    /// <summary>Minimum gap left between moved text and the container it was crossing.</summary>
    public const double Clearance = 0.06;

    /// <summary>Moves straddling text blocks clear of the containers they cross.</summary>
    public static int AvoidContainers(Diagram diagram)
    {
        int moved = 0;

        foreach (var page in diagram.Pages)
        {
            var groups = page.Groups.Where(g => g.Bounds is not null).ToList();
            if (groups.Count == 0) continue;

            foreach (var text in page.TextBlocks)
            {
                if (string.IsNullOrWhiteSpace(text.Text)) continue;

                for (int pass = 0; pass < 4; pass++)
                {
                    if (!TryClear(text, groups)) break;
                    moved++;
                }
            }
        }

        return moved;
    }

    /// <summary>True for the roles that belong to the page rather than to a band.</summary>
    private static bool IsPageHeading(TextBlock text) =>
        string.Equals(text.Role, "title", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text.Role, "subtitle", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Moves <paramref name="text"/> up past the highest border it straddles.
    /// Returns false when it straddles nothing.
    /// </summary>
    private static bool TryClear(TextBlock text, List<Group> groups)
    {
        double highestTop = double.NegativeInfinity;
        bool straddles = false;

        foreach (var group in groups)
        {
            var g = group.Bounds!;

            bool overlapsX = text.Bounds.X + text.Bounds.W / 2 > g.X - g.W / 2
                          && text.Bounds.X - text.Bounds.W / 2 < g.X + g.W / 2;
            if (!overlapsX) continue;

            double textBottom = text.Bounds.Y - text.Bounds.H / 2;
            double textTop = text.Bounds.Y + text.Bounds.H / 2;
            double groupBottom = g.Y - g.H / 2;
            double groupTop = g.Y + g.H / 2;

            if (textTop <= groupBottom || textBottom >= groupTop) continue;

            // Wholly inside: the author meant it — unless this is a page title, which the layout can swallow.
            //
            // A band is derived from its members and grows upward, so it can close over a title that was clear
            // when the author placed it. To this test the result is indistinguishable from a deliberate inside
            // label: the geometry says "contained". The difference is authorship, which the model does not
            // record, so role is the closest available proxy — a band never authors a title inside itself.
            if (textBottom >= groupBottom && textTop <= groupTop && !IsPageHeading(text)) continue;

            straddles = true;
            highestTop = Math.Max(highestTop, groupTop);
        }

        if (!straddles) return false;

        double needed = highestTop + Clearance + text.Bounds.H / 2;
        text.Bounds.Y = Math.Round(needed * 40) / 40;
        return true;
    }

    /// <summary>
    /// Positions where a label can be placed when it needs to move: offset from the midpoint, far
    /// enough apart to separate two labels of ordinary width.
    /// </summary>
    private static readonly double[] LabelSlots = { 0.25, 0.75, 0.15, 0.85 };

    /// <summary>
    /// Re-centres page-level headings on the page they actually ended up on.
    ///
    /// A title is authored against the page the caller imagined. `canvas.shrinkToFit` then trims the page to the
    /// content, and the title's absolute x no longer matches the middle of anything — measured, `cbo` sits 0.72"
    /// right of centre and `user-data-flow` 0.74" left.
    ///
    /// `align: center` does not fix this: it centres the text inside its box, and the box is what moved.
    ///
    /// Only `title` and `subtitle` with a centred alignment are touched. A left-aligned heading keeps its x
    /// because there the position is part of the request, and a heading wider than the page is left alone —
    /// centring it would push part of it off each edge, which hides the overflow instead of showing it.
    /// </summary>
    public static int CentreTitles(Diagram diagram)
    {
        int moved = 0;

        foreach (var page in diagram.Pages)
        {
            double width = page.Size?.Width ?? 0;
            if (width <= 0) continue;

            foreach (var text in page.TextBlocks)
            {
                if (text.Bounds.W > width) continue;
                if (!string.Equals(text.Align, "center", StringComparison.OrdinalIgnoreCase)) continue;

                bool heading = string.Equals(text.Role, "title", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(text.Role, "subtitle", StringComparison.OrdinalIgnoreCase);
                if (!heading) continue;

                // No snapping: rounding to 1/40" left `user-data-flow` 0.01" off centre, and a heading is the
                // one element where a visible asymmetry is most obvious.
                double centre = width / 2;
                if (Math.Abs(text.Bounds.X - centre) < 0.01) continue;

                text.Bounds.X = centre;
                moved++;
            }
        }

        return moved;
    }

    /// <summary>
    /// Lifts a text block clear of nodes it sits on top of.
    ///
    /// `AvoidContainers` handles band borders and `SpreadLabels` handles connector labels; neither looks at nodes,
    /// so a legend whose box lands inside a band is drawn over whatever is there. The analyser reports it as
    /// `text.overlaps-node` and nothing acts on that.
    ///
    /// The block is raised to just above the highest node it overlaps, keeping its x so the author's horizontal
    /// placement survives. If raising it would push it off the page, it is left alone — a reported overlap beats
    /// an unreported clipping.
    /// </summary>
    public static int AvoidNodes(Diagram diagram)
    {
        int moved = 0;

        foreach (var page in diagram.Pages)
        {
            if (page.Nodes.Count == 0) continue;

            double pageTop = page.Size?.Height ?? double.MaxValue;

            foreach (var text in page.TextBlocks)
            {
                if (string.IsNullOrWhiteSpace(text.Text)) continue;

                for (int pass = 0; pass < 4; pass++)
                {
                    var hit = page.Nodes
                        .Where(n => n.Bounds is not null && Overlaps(text.Bounds, n.Bounds))
                        .OrderByDescending(n => n.Bounds!.Y + n.Bounds!.H / 2)
                        .FirstOrDefault();

                    if (hit is null) break;

                    double wanted = hit.Bounds!.Y + hit.Bounds!.H / 2
                                  + Clearance + text.Bounds.H / 2;

                    // Off the page already: leave it, the overlap is the lesser problem.
                    if (wanted + text.Bounds.H / 2 > pageTop) break;
                    if (Math.Abs(wanted - text.Bounds.Y) < 0.01) break;

                    text.Bounds.Y = Math.Round(wanted * 40) / 40;
                    moved++;
                }
            }
        }

        return moved;
    }

    /// <summary>True when two boxes share area.</summary>
    private static bool Overlaps(Bounds a, Bounds b)
    {
        bool x = Math.Abs(a.X - b.X) < (a.W + b.W) / 2;
        bool y = Math.Abs(a.Y - b.Y) < (a.H + b.H) / 2;
        return x && y;
    }

    /// <summary>
    /// Separates edge labels that landed on each other, by moving the ones the author did not place.
    ///
    /// A label without an explicit position is a default, not a decision, so it is the one that
    /// gives way. Two labels the author positioned deliberately stay put even if they overlap: the
    /// analyser still reports them, but silently overriding a stated choice would be worse.
    ///
    /// Each candidate position is checked against the whole page before being kept, so the method
    /// either reduces the overlaps or leaves the diagram alone.
    /// </summary>
    public static int SpreadLabels(Diagram diagram)
    {
        int moved = 0;

        foreach (var page in diagram.Pages)
        {
            // Repeat while progress is being made: moving one label can clear two collisions, and a
            // single pass over a stale list would miss the second.
            for (int pass = 0; pass < 4; pass++)
            {
                if (Overlapping(diagram, page) is not { Count: > 0 } collisions) break;

                bool progressed = false;
                foreach (var edge in collisions)
                {
                    if (edge.LabelPosition is not null) continue;

                    foreach (var slot in LabelSlots)
                    {
                        edge.LabelPosition = slot;
                        if (Overlapping(diagram, page).Count < collisions.Count) { progressed = true; break; }
                        edge.LabelPosition = null;
                    }

                    if (progressed) { moved++; break; }
                }

                if (!progressed) break;
            }
        }

        return moved;
    }

    /// <summary>Edges of this page whose label currently collides with another's.</summary>
    private static List<Edge> Overlapping(Diagram diagram, Page page)
    {
        var report = LayoutAnalyzer.Analyze(diagram);
        var ids = report.Issues
            .Where(i => i.Code == "label.overlaps-label")
            .SelectMany(i => i.Targets ?? Array.Empty<string>())
            .ToHashSet(StringComparer.Ordinal);

        return page.Edges.Where(e => ids.Contains(e.Id)).ToList();
    }

    /// <summary>
    /// Keeps decorations on the sheet without collapsing their spacing.
    ///
    /// Each block used to be clamped on its own, so everything overflowing the same edge landed on
    /// the same boundary value: measured on the arch example, a title and subtitle authored 0.6"
    /// apart came out 0.1" apart with a 0.40" overlap. The author's relative spacing is intent, so
    /// blocks that overflow the same edge move together, and only by enough to bring the outermost
    /// one inside.
    /// </summary>
    /// <summary>
    /// Shifts every object on the page so the drawing sits centred on the sheet.
    ///
    /// Needed after the paper is chosen: layout positions are relative to the content's own bounding
    /// box, so putting them on a standard sheet unchanged leaves the drawing flush against one edge.
    /// Measured on the deploy topology, 4.25" of content on a 16.54" sheet left 8.11" of blank space
    /// above and none below.
    /// </summary>
    public static void CenterContent(Page page)
    {
        if (page.Size is not { Width: > 0, Height: > 0 } size) return;

        var boxes = page.Nodes.Select(n => n.Bounds)
            .Concat(page.Groups.Select(g => g.Bounds))
            .Concat(page.TextBlocks.Select(t => t.Bounds))
            .Concat(page.Paths.Select(p => p.Bounds))
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();

        if (boxes.Count == 0) return;

        double left = boxes.Min(b => b.X - b.W / 2);
        double right = boxes.Max(b => b.X + b.W / 2);
        double bottom = boxes.Min(b => b.Y - b.H / 2);
        double top = boxes.Max(b => b.Y + b.H / 2);

        double dx = size.Width / 2 - (left + right) / 2;
        double dy = size.Height / 2 - (bottom + top) / 2;

        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;

        foreach (var b in boxes) b.X += dx;
        foreach (var b in boxes) b.Y += dy;

        // Waypoints are absolute; leaving them behind would route edges through empty space.
        foreach (var edge in page.Edges.Where(e => e.Waypoints is { Count: > 0 }))
            foreach (var point in edge.Waypoints!)
            {
                point.X += dx;
                point.Y += dy;
            }
    }

    public static void ClampToPage(Diagram diagram)
    {
        foreach (var page in diagram.Pages)
        {
            if (page.Size is not { Width: > 0, Height: > 0 } size) continue;

            ClampGroup(page.TextBlocks.Select(t => t.Bounds).ToList(), size);
            ClampGroup(page.Paths.Select(p => p.Bounds).ToList(), size);
        }
    }

    private static void ClampGroup(List<Bounds> boxes, PageSize size)
    {
        if (boxes.Count == 0) return;

        const double Margin = DiagramSizer.PageMargin;

        // Horizontal: shift the whole set by the smallest amount that brings every box inside.
        double left = boxes.Min(b => b.X - b.W / 2);
        double right = boxes.Max(b => b.X + b.W / 2);

        if (left < Margin)
        {
            double dx = Margin - left;
            foreach (var b in boxes) b.X += dx;
        }
        else if (right > size.Width - Margin)
        {
            double dx = right - (size.Width - Margin);
            foreach (var b in boxes) b.X -= dx;
        }

        double bottom = boxes.Min(b => b.Y - b.H / 2);
        double top = boxes.Max(b => b.Y + b.H / 2);

        if (top > size.Height - Margin)
        {
            double dy = top - (size.Height - Margin);
            foreach (var b in boxes) b.Y -= dy;
        }
        else if (bottom < Margin)
        {
            double dy = Margin - bottom;
            foreach (var b in boxes) b.Y += dy;
        }

        // Round once, at the end, so the shift stays uniform.
        foreach (var b in boxes)
        {
            b.X = Math.Round(b.X * 40) / 40;
            b.Y = Math.Round(b.Y * 40) / 40;
        }
    }
}
