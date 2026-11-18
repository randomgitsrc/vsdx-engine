using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>How a diagram's nodes should be arranged.</summary>
public enum LayoutStrategy
{
    /// <summary>Keep the coordinates exactly as the model supplies them.</summary>
    Manual,

    /// <summary>
    /// Bands stacked top-to-bottom, nodes ordered left-to-right inside each band.
    /// The natural shape for layered architectures and deployment tiers.
    /// </summary>
    Layered,

    /// <summary>Uniform rows and columns; good for dashboards and inventories.</summary>
    Grid,

    /// <summary>
    /// Follow the edge direction: sources first, then each step to the right.
    /// Suits pipelines and left-to-right business flows.
    /// </summary>
    Flow,

    /// <summary>Hierarchy from a root, widening downward; suits org charts.</summary>
    Tree,

    /// <summary>
    /// Cross-functional swimlanes: horizontal bands with independent left-to-right flows inside
    /// each. Selected automatically when the page declares <c>Kind = "lane"</c> containers.
    /// </summary>
    Swimlane
}

/// <summary>Primary reading direction.</summary>
public enum LayoutDirection
{
    LeftToRight,
    TopToBottom
}

public sealed record LayoutOptions
{
    public LayoutStrategy Strategy { get; init; } = LayoutStrategy.Layered;
    public LayoutDirection Direction { get; init; } = LayoutDirection.LeftToRight;

    /// <summary>Horizontal space between adjacent nodes in a band.</summary>
    public double NodeGapX { get; init; } = 0.55;

    /// <summary>Vertical space between bands.</summary>
    public double BandGapY { get; init; } = 0.85;

    /// <summary>Space between rows when a strategy wraps a band onto several rows.</summary>
    public double RowGapY { get; init; } = 0.45;

    /// <summary>Largest number of nodes allowed on one row before wrapping.</summary>
    public int MaxNodesPerRow { get; init; } = 6;

    /// <summary>Page border kept clear of content.</summary>
    public double PageMargin { get; init; } = DiagramSizer.PageMargin;

    /// <summary>Type scale used for container headings and any wrapping.</summary>
    public TypeScale? Scale { get; init; }

    /// <summary>Explicit layer assignment, keyed by node id. Overrides group-derived layers.</summary>
    public IReadOnlyDictionary<string, int>? LayerOfNode { get; init; }
}

/// <summary>
/// Places nodes on the page. Strategies are interchangeable so the design layer can pick one
/// from the diagram's semantics rather than every diagram going through one code path.
/// </summary>
public interface ILayoutEngine
{
    LayoutStrategy Strategy { get; }

    /// <summary>
    /// Repositions nodes and containers on <paramref name="page"/>. Implementations must be
    /// deterministic: the same input must always yield the same coordinates.
    /// </summary>
    LayoutResult Layout(Page page, LayoutOptions options);
}

public sealed class LayoutResult
{
    public LayoutStrategy Strategy { get; init; }
    public int Layers { get; init; }
    public int NodesPlaced { get; init; }
    public double ContentWidth { get; init; }
    public double ContentHeight { get; init; }
    public List<string> Notes { get; } = new();
}

/// <summary>Chooses and runs a layout strategy.</summary>
public static class DiagramLayoutEngine
{
    /// <summary>
    /// Picks a strategy from the diagram's declared kind. The kind field was previously unused;
    /// wiring it here is what makes "different diagram types get different treatment" true.
    /// </summary>
    public static LayoutStrategy SuggestStrategy(Diagram diagram)
    {
        var page = diagram.Pages.FirstOrDefault();
        if (page is null) return LayoutStrategy.Manual;

        // Declared lanes are an explicit request for swimlane treatment, which outranks the
        // diagram's nominal kind: the author has already said how the page is organised.
        if (page.Groups.Any(g => string.Equals(g.Kind, "lane", StringComparison.OrdinalIgnoreCase)))
            return LayoutStrategy.Swimlane;

        // A business flow with decisions reads along its edges, not as bands.
        bool hasDecisions = page.Nodes.Any(n =>
            string.Equals(n.Shape, "Diamond", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.Shape, "Decision", StringComparison.OrdinalIgnoreCase));

        return diagram.Type?.ToLowerInvariant() switch
        {
            "workflow" or "flow" => hasDecisions ? LayoutStrategy.Flow : LayoutStrategy.Layered,
            "org" or "organisation" or "organization" or "orgchart" => LayoutStrategy.Tree,
            "network" => LayoutStrategy.Layered,
            "microservices" => LayoutStrategy.Layered,
            "architecture" => LayoutStrategy.Layered,
            _ => page.Groups.Count > 0 ? LayoutStrategy.Layered : LayoutStrategy.Grid
        };
    }

    /// <summary>
    /// True when the caller asked for a top-to-bottom layout under Manual, which cannot be
    /// honoured: manual means the author's coordinates win. Reported by the caller as unmet
    /// rather than silently ignored.
    /// </summary>
    public static bool IsDirectionIgnoredUnderManual(LayoutOptions options, LayoutStrategy strategy) =>
        options.Direction == LayoutDirection.TopToBottom && strategy == LayoutStrategy.Manual;

    /// <summary>Runs the requested strategy. Manual is a no-op.</summary>
    public static LayoutResult Apply(
        Diagram diagram, LayoutOptions? options = null, LayoutStrategy? strategyOverride = null)
    {
        options ??= new LayoutOptions();
        var strategy = strategyOverride ?? options.Strategy;
        var engine = Create(strategy);

        var results = new List<LayoutResult>();
        foreach (var page in diagram.Pages)
        {
            // Author waypoints are absolute coordinates. Once nodes move they describe a route
            // between positions that no longer exist, so the router must be free to re-route.
            foreach (var edge in page.Edges)
                edge.Waypoints = null;

            var pageOptions = options with { Strategy = strategy };

            // Text blocks and paths are positioned against the page, not against the node graph:
            // a title belongs at the top of the sheet wherever the services end up. Layout only
            // moves nodes, so decorations are left where the caller put them — but they must be
            // clamped into the sheet afterwards, or a title at the author's y=8.5 can end up
            // outside a page that shrank to 6 inches.
            results.Add(engine.Layout(page, pageOptions));
            ClampDecorationsToPage(page);
        }

        // Decorations are clamped to the sheet and then pushed clear of band borders: a container
        // derived from its members can grow into a title placed earlier, and the page clamp alone
        // does not notice.
        DecorationLayout.AvoidContainers(diagram);

        // Direction is applied once for the whole document, after every page has been laid out.
        //
        // Transposing inside the loop above would transpose all pages on every iteration: the
        // reflection covers the entire diagram, not the page being processed. A two-page document
        // would be reflected twice and come back to its original orientation while a three-page
        // one flipped twice more — pages would disagree with each other about which way is up.
        //
        // Manual is deliberately excluded: its contract is that the author's coordinates are
        // final, so reflecting them would break the one guarantee manual makes. The caller
        // reports that as unmet rather than applying it silently.
        if (options.Direction == LayoutDirection.TopToBottom && strategy != LayoutStrategy.Manual)
        {
            DiagramLayout.Transpose(diagram);
        }

        return results.FirstOrDefault() ?? new LayoutResult { Strategy = strategy };
    }

    /// <summary>
    /// Keeps standalone text and paths inside the page after layout.
    ///
    /// Decorations are deliberately not part of the layout graph: moving a title because a service
    /// moved would be wrong. But leaving them at coordinates that the page no longer covers makes
    /// them invisible in the deliverable, which is worse. Nudging them inside is the honest
    /// middle: position is honoured where possible, corrected only when it would be lost.
    /// </summary>
    private static void ClampDecorationsToPage(Page page)
    {
        if (page.Size is not { Width: > 0 } size) return;

        const double margin = DiagramSizer.PageMargin;

        static double Clamp(double value, double half, double limit) =>
            Math.Max(half + margin, Math.Min(value, limit - half - margin));

        foreach (var text in page.TextBlocks)
        {
            text.Bounds.X = Math.Round(Clamp(text.Bounds.X, text.Bounds.W / 2, size.Width) * 40) / 40;
            text.Bounds.Y = Math.Round(Clamp(text.Bounds.Y, text.Bounds.H / 2, size.Height) * 40) / 40;
        }

        foreach (var path in page.Paths)
        {
            path.Bounds.X = Math.Round(Clamp(path.Bounds.X, path.Bounds.W / 2, size.Width) * 40) / 40;
            path.Bounds.Y = Math.Round(Clamp(path.Bounds.Y, path.Bounds.H / 2, size.Height) * 40) / 40;
        }
    }

    /// <summary>Resolves a strategy name to an engine.</summary>
    public static ILayoutEngine Create(LayoutStrategy strategy) => strategy switch
    {
        LayoutStrategy.Layered => new LayeredLayoutEngine(),
        LayoutStrategy.Grid => new GridLayoutEngine(),
        LayoutStrategy.Flow => new FlowLayoutEngine(),
        LayoutStrategy.Tree => new TreeLayoutEngine(),
        LayoutStrategy.Swimlane => new SwimlaneLayoutEngine(),
        _ => new ManualLayoutEngine()
    };

    /// <summary>Parses a strategy name from CLI or JSON input.</summary>
    /// <summary>
    /// Parses a strategy name, reporting whether it was recognised.
    ///
    /// The old signature returned Layered for anything it did not know, so a caller could not tell
    /// a deliberate request from a typo — and the diagram ran a different strategy than the one
    /// asked for, silently. Falling back is fine; falling back without saying so is not.
    /// </summary>
    public static bool TryParse(string? value, out LayoutStrategy strategy)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "layered" or "layer" or "hierarchical": strategy = LayoutStrategy.Layered; return true;
            case "grid": strategy = LayoutStrategy.Grid; return true;
            case "flow" or "pipeline": strategy = LayoutStrategy.Flow; return true;
            case "tree" or "orgchart": strategy = LayoutStrategy.Tree; return true;
            case "swimlane" or "lane" or "lanes": strategy = LayoutStrategy.Swimlane; return true;
            case "manual" or "none": strategy = LayoutStrategy.Manual; return true;
            default: strategy = LayoutStrategy.Layered; return false;
        }
    }

    /// <summary>Parses a strategy name, falling back to Layered. Use <see cref="TryParse"/> when
    /// the caller needs to know the value was not understood.</summary>
    public static LayoutStrategy Parse(string? value) => TryParse(value, out var strategy) ? strategy : strategy;
}
