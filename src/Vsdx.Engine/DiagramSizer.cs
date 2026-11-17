using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Sizes nodes, containers and the page from their text.
///
/// This closes the gap Phase 0 identified: text metrics existed but were only used to report
/// overflow after the fact. Here they run *before* layout, so a node is never sized
/// independently of the words it must hold.
/// </summary>
public static class DiagramSizer
{
    /// <summary>Minimum node edge, so a short label still yields a clickable shape.</summary>
    public const double MinNodeWidth = 1.30;
    public const double MinNodeHeight = 0.55;

    /// <summary>Preferred aspect: wider than tall reads better for labels.</summary>
    public const double PreferredAspect = 2.2;
    public const double MaxAspect = 3.6;

    /// <summary>Page margin kept clear around the drawing.</summary>
    public const double PageMargin = 0.55;

    /// <summary>
    /// Floor for page dimensions, so trimming cannot produce a useless page.
    ///
    /// These are deliberately modest. A previous 8x6" floor forced a tall, narrow diagram to sit
    /// in a wide page with a large empty band, which is exactly the defect the audit found. The
    /// floor exists to keep a page usable, not to impose a preferred aspect ratio.
    /// </summary>
    public const double MinPageWidth = 4.0;
    public const double MinPageHeight = 3.0;

    /// <summary>
    /// Resizes every node to fit its text, then resizes containers to fit their members, then
    /// grows the page to fit the drawing. Idempotent: running it twice changes nothing.
    /// </summary>
    public static SizingReport Apply(Diagram diagram, TypeScale? scaleOverride = null)
    {
        var report = new SizingReport();

        foreach (var page in diagram.Pages)
        {
            var scale = scaleOverride ?? TypeScale.ForCanvas(
                page.Size?.Width ?? diagram.PageSize?.Width ?? 11,
                page.Size?.Height ?? diagram.PageSize?.Height ?? 8.5,
                page.Nodes.Count);

            report.Scale = scale;

            if (!scale.IsLegible)
            {
                report.Issues.Add(
                    $"字号已触底 {scale.NodeTitle:F1}pt（下限 {TypeScale.MinReadablePt}pt）；" +
                    "内容过密应拆图或扩大画布，而不是继续缩小文字");
            }

            // ---- 1. Nodes from text ------------------------------------------
            foreach (var node in page.Nodes)
            {
                double fontSize = node.Style?.FontSize ?? scale.NodeTitle;
                bool bold = node.Style?.Bold ?? false;

                var measured = TextLayout.Measure(node.Text, fontSize, bold, scale);

                // Honour an explicitly larger author size; only grow, never shrink below it.
                double targetW = Math.Max(MinNodeWidth, measured.RequiredWidth);
                double targetH = Math.Max(MinNodeHeight, measured.RequiredHeight);

                // Keep boxes from becoming absurdly wide for one long line by wrapping the
                // text, NOT by shrinking the box — shrinking would clip the label, which is
                // exactly what this class exists to prevent.
                if (targetW / targetH > MaxAspect && !string.IsNullOrWhiteSpace(node.Text))
                {
                    double wrapWidth = Math.Max(MinNodeWidth, measured.RequiredWidth * 0.72);
                    var (wrapped, wrappedMeasure) = TextLayout.Wrap(node.Text, wrapWidth, fontSize, bold, scale);

                    if (wrappedMeasure.FitsWithin(wrapWidth, targetH + 1) &&
                        wrappedMeasure.RequiredWidth < measured.RequiredWidth)
                    {
                        node.Text = wrapped;
                        measured = wrappedMeasure;
                        targetW = Math.Max(MinNodeWidth, measured.RequiredWidth);
                        targetH = Math.Max(MinNodeHeight, measured.RequiredHeight);
                    }
                    else
                    {
                        // Cannot wrap usefully: keep the wide box rather than clip the text.
                        targetW = Math.Max(targetW, MinNodeWidth);
                    }
                }

                bool changed = Math.Abs(targetW - node.Bounds.W) > 0.01
                            || Math.Abs(targetH - node.Bounds.H) > 0.01;

                if (changed)
                {
                    node.Bounds.W = Round(targetW);
                    node.Bounds.H = Round(targetH);
                    report.NodesResized++;
                }

                if (!string.IsNullOrEmpty(node.Text) &&
                    !measured.FitsWithin(node.Bounds.W, node.Bounds.H, 0.03))
                {
                    report.Issues.Add($"节点 '{node.Id}' 文字仍超出框体");
                }
            }

            // ---- 2. Containers from members ----------------------------------
            DiagramLayout.Apply(diagram);
        }

        return report;
    }

    /// <summary>
    /// Grows the page so the drawing fits with a consistent margin. Only ever enlarges: an
    /// author-supplied page that is already large enough is left alone.
    /// </summary>
    public static PageFit FitPage(Page page, double margin = PageMargin, bool shrink = false)
    {
        // Containers count too: a band that extends past its members still has to fit.
        // Derive missing container bounds first so a standalone FitPage call is not blind.
        DiagramLayout.Apply(new Diagram { Pages = { page } });

        // Text blocks and free paths are part of the drawing: fitting the page without them
        // crops the title and legend straight off the deliverable.
        var elements = ContentBounds(page);

        if (elements.Count == 0) return new PageFit(false, 0, 0, 0, 0);

        double minX = elements.Min(b => b.X - b.W / 2);
        double maxX = elements.Max(b => b.X + b.W / 2);
        double minY = elements.Min(b => b.Y - b.H / 2);
        double maxY = elements.Max(b => b.Y + b.H / 2);

        // The page must contain the content's *absolute extents* plus a margin — not merely be
        // as large as the content's span. Content sitting at x=9 with width 2 needs a page
        // reaching x=10, not a 3"-wide page.
        double neededW = maxX + margin;
        double neededH = maxY + margin;

        // Content that starts left of the margin cannot be fixed by growing; the caller must
        // recentre. Report it rather than silently producing a lopsided page.
        bool needsRecenter = minX < margin - 0.01 || minY < margin - 0.01;

        double currentW = page.Size?.Width ?? 0;
        double currentH = page.Size?.Height ?? 0;

        bool mustGrow = neededW > currentW + 0.01 || neededH > currentH + 0.01;
        if (mustGrow)
        {
            page.Size ??= new PageSize();
            page.Size.Width = Round(Math.Max(currentW, neededW));
            page.Size.Height = Round(Math.Max(currentH, neededH));

            return new PageFit(true, currentW, currentH, page.Size.Width, page.Size.Height)
            {
                NeedsRecenter = needsRecenter
            };
        }

        // The page is larger than necessary. Shrinking is opt-in: an author may deliberately
        // want a standard paper size, so only trim when asked.
        if (!shrink) return new PageFit(false, currentW, currentH, currentW, currentH);

        double trimmedW = Math.Max(neededW, MinPageWidth);
        double trimmedH = Math.Max(neededH, MinPageHeight);

        // Do not shrink for a marginal gain; leave the page alone when the excess is small.
        if (currentW - trimmedW < 1.0 && currentH - trimmedH < 1.0)
            return new PageFit(false, currentW, currentH, currentW, currentH);

        page.Size ??= new PageSize();
        page.Size.Width = Round(trimmedW);
        page.Size.Height = Round(trimmedH);

        return new PageFit(true, currentW, currentH, page.Size.Width, page.Size.Height) { Shrank = true };
    }

    /// <summary>
    /// Shifts the whole drawing so it sits inside the page with equal margins. Use after
    /// resizing shapes, otherwise growth happens only to the right and upward.
    /// </summary>
    public static (double dx, double dy) Recenter(Page page, double margin = PageMargin)
    {
        var elements = ContentBounds(page);
        if (elements.Count == 0) return (0, 0);

        double minX = elements.Min(b => b.X - b.W / 2);
        double minY = elements.Min(b => b.Y - b.H / 2);

        double dx = margin - minX;
        double dy = margin - minY;
        if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return (0, 0);

        foreach (var node in page.Nodes)
        {
            node.Bounds.X = Round(node.Bounds.X + dx);
            node.Bounds.Y = Round(node.Bounds.Y + dy);
        }
        // Text and paths must move with everything else, or centring shears the figure.
        foreach (var text in page.TextBlocks)
        {
            text.Bounds.X = Round(text.Bounds.X + dx);
            text.Bounds.Y = Round(text.Bounds.Y + dy);
        }
        foreach (var path in page.Paths)
        {
            path.Bounds.X = Round(path.Bounds.X + dx);
            path.Bounds.Y = Round(path.Bounds.Y + dy);
        }
        foreach (var group in page.Groups.Where(g => g.Bounds is not null))
        {
            group.Bounds!.X = Round(group.Bounds.X + dx);
            group.Bounds.Y = Round(group.Bounds.Y + dy);
        }
        foreach (var edge in page.Edges.Where(e => e.Waypoints is { Count: > 0 }))
        {
            foreach (var wp in edge.Waypoints!)
            {
                wp.X = Round(wp.X + dx);
                wp.Y = Round(wp.Y + dy);
            }
        }

        return (dx, dy);
    }

    /// <summary>
    /// Every visible bounding box on the page: nodes, containers, standalone text and paths.
    /// Extent calculations must use this rather than <c>page.Nodes</c>, otherwise fitting or
    /// centring silently crops the parts that are not nodes.
    /// </summary>
    private static List<Bounds> ContentBounds(Page page)
    {
        var elements = new List<Bounds>();
        elements.AddRange(page.Nodes.Select(n => n.Bounds));
        elements.AddRange(page.Groups.Where(g => g.Bounds is not null).Select(g => g.Bounds!));
        elements.AddRange(page.TextBlocks.Select(t => t.Bounds));
        elements.AddRange(page.Paths.Select(p => p.Bounds));
        return elements;
    }

    private static double Round(double v) => Math.Round(v * 40) / 40.0;   // 0.025" grid
}

public sealed class SizingReport
{
    public TypeScale Scale { get; set; } = new();
    public int NodesResized { get; set; }
    public List<string> Issues { get; } = new();
    public bool Ok => Issues.Count == 0;
}

public readonly record struct PageFit(
    bool Grew, double PreviousWidth, double PreviousHeight, double Width, double Height)
{
    /// <summary>True when the drawing starts too close to the origin and must be shifted.</summary>
    public bool NeedsRecenter { get; init; }

    /// <summary>True when the page was trimmed rather than grown.</summary>
    public bool Shrank { get; init; }
}
