using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Places a legend in the page's bottom margin.
///
/// The legend is emitted as ordinary drawing content — a filled swatch and a label per row — rather than as a new
/// object kind. Everything below the drawing already exists in the model, and a legend that used a special kind
/// would need its own writer, reader and renderer support.
///
/// Rows are laid out left to right with a fixed pitch, wrapping when the page runs out of width. The vertical
/// position is below the lowest drawn element so the legend never competes with the diagram.
/// </summary>
public static class LegendLayout
{
    private const double RowHeight = 0.40;
    private const double SwatchWidth = 0.26;
    private const double GapAfterSwatch = 0.10;
    private const double GapAfterLabel = 0.34;
    private const double BottomMargin = 0.45;

    /// <summary>
    /// A small outline matching the shape being described, in the swatch's own local coordinates.
    ///
    /// Rectangles and process boxes share an outline; a parallelogram leans, and a hexagon has clipped ends. Any
    /// other master falls back to the rectangle, which is what most of them are.
    /// </summary>
    private static PathGeometry SwatchGeometry(string shape)
    {
        double w = SwatchWidth, h = 0.16;

        List<PathRow> rows = shape?.Trim().ToLowerInvariant() switch
        {
            "parallelogram" => new List<PathRow>
            {
                new() { Type = "MoveTo", Cells = { ["X"] = $"{w * 0.22:0.###}", ["Y"] = "0" } },
                new() { Type = "LineTo", Cells = { ["X"] = $"{w:0.###}", ["Y"] = "0" } },
                new() { Type = "LineTo", Cells = { ["X"] = $"{w * 0.78:0.###}", ["Y"] = $"{h:0.###}" } },
                new() { Type = "LineTo", Cells = { ["X"] = "0", ["Y"] = $"{h:0.###}" } },
                new() { Type = "LineTo", Cells = { ["X"] = $"{w * 0.22:0.###}", ["Y"] = "0" } }
            },
            "hexagon" => new List<PathRow>
            {
                new() { Type = "MoveTo", Cells = { ["X"] = $"{w * 0.25:0.###}", ["Y"] = "0" } },
                new() { Type = "LineTo", Cells = { ["X"] = $"{w * 0.75:0.###}", ["Y"] = "0" } },
                new() { Type = "LineTo", Cells = { ["X"] = $"{w:0.###}", ["Y"] = $"{h / 2:0.###}" } },
                new() { Type = "LineTo", Cells = { ["X"] = $"{w * 0.75:0.###}", ["Y"] = $"{h:0.###}" } },
                new() { Type = "LineTo", Cells = { ["X"] = $"{w * 0.25:0.###}", ["Y"] = $"{h:0.###}" } },
                new() { Type = "LineTo", Cells = { ["X"] = "0", ["Y"] = $"{h / 2:0.###}" } },
                new() { Type = "LineTo", Cells = { ["X"] = $"{w * 0.25:0.###}", ["Y"] = "0" } }
            },
            _ => new List<PathRow>
            {
                new() { Type = "MoveTo", Cells = { ["X"] = "0", ["Y"] = "0" } },
                new() { Type = "LineTo", Cells = { ["X"] = $"{w:0.###}", ["Y"] = "0" } },
                new() { Type = "LineTo", Cells = { ["X"] = $"{w:0.###}", ["Y"] = $"{h:0.###}" } },
                new() { Type = "LineTo", Cells = { ["X"] = "0", ["Y"] = $"{h:0.###}" } },
                new() { Type = "LineTo", Cells = { ["X"] = "0", ["Y"] = "0" } }
            }
        };

        return new PathGeometry { Rows = rows };
    }

    /// <summary>
    /// Adds legend rows to the page. Returns the number of entries drawn.
    ///
    /// A diagram with fewer than two distinct roles gets no legend: one colour needs no key, and adding one is the
    /// kind of decoration that makes a drawing look generated.
    /// </summary>
    public static int Apply(Diagram diagram, string? paletteName)
    {
        var entries = LegendBuilder.Entries(diagram, paletteName);
        if (entries.Count < 2) return 0;

        int drawn = 0;

        foreach (var page in diagram.Pages)
        {
            if (page.Nodes.Count == 0) continue;

            // Containers count: a band extends below its members, and anchoring to the nodes put the legend
            // across the bottom band's border — measured, three labels straddling it and a 100-point diagram
            // scoring 88.
            var boxes = page.Nodes.Select(n => n.Bounds)
                .Concat(page.Groups.Where(g => g.Bounds is not null).Select(g => g.Bounds!))
                .ToList();

            double left = boxes.Min(b => b.X - b.W / 2);
            double right = boxes.Max(b => b.X + b.W / 2);
            double bottom = boxes.Min(b => b.Y - b.H / 2);

            double x = left;
            double y = bottom - BottomMargin;

            foreach (var entry in entries)
            {
                double labelWidth = Math.Max(0.55, TextLayout.Measure(entry.Label, 8).RequiredWidth);

                // Wrap rather than run off the page.
                if (x + SwatchWidth + GapAfterSwatch + labelWidth > right && x > left)
                {
                    x = left;
                    y -= RowHeight;
                }

                var swatch = new FreePath
                {
                    Id = $"legend-swatch-{page.Name}-{entry.Role}",
                    Bounds = new Bounds(x + SwatchWidth / 2, y + 0.08, SwatchWidth, 0.16),
                    Style = new ShapeStyle { Fill = entry.Fill, Stroke = entry.Fill },

                    // The swatch carries the shape it stands for. Colour alone is not enough: `queue`, `database`
                    // and `storage` all resolve to the same orange, so three identical blocks with three different
                    // labels told the reader the colours meant three things when they meant one.
                    Geometry = { SwatchGeometry(entry.Shape) }
                };

                var label = new TextBlock
                {
                    Id = $"legend-label-{page.Name}-{entry.Role}",
                    Text = entry.Label,
                    Role = "legend",
                    Bounds = new Bounds(
                        x + SwatchWidth + GapAfterSwatch + labelWidth / 2,
                        y + 0.16,
                        labelWidth,
                        0.24),
                    Align = "left"
                };

                page.Paths.Add(swatch);
                page.TextBlocks.Add(label);
                drawn++;

                x += SwatchWidth + GapAfterSwatch + labelWidth + GapAfterLabel;
            }
        }

        return drawn;
    }
}
