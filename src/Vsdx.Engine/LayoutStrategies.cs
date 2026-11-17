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
            results.Add(engine.Layout(page, pageOptions));
        }

        return results.FirstOrDefault() ?? new LayoutResult { Strategy = strategy };
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
    public static LayoutStrategy Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "layered" or "layer" or "hierarchical" => LayoutStrategy.Layered,
        "grid" => LayoutStrategy.Grid,
        "flow" or "pipeline" => LayoutStrategy.Flow,
        "tree" or "orgchart" => LayoutStrategy.Tree,
        "swimlane" or "lane" or "lanes" => LayoutStrategy.Swimlane,
        "manual" or "none" => LayoutStrategy.Manual,
        _ => LayoutStrategy.Layered
    };
}
