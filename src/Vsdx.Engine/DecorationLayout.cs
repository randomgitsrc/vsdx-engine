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

            // Wholly inside: the author meant it.
            if (textBottom >= groupBottom && textTop <= groupTop) continue;

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
}
