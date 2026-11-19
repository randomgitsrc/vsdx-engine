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
            // A workflow without containers reads along its edges whatever its nodes look like.
            // Requiring a decision diamond meant a pure data flow fell through to Layered, where a
            // chain of eight nodes became eight layers: measured at 23.3" x 7.5", a 3.1:1 strip,
            // against 8.5" x 15.6" from the flow engine — which also scored higher, so the strip
            // was not buying legibility.
            "workflow" or "flow" => hasDecisions || page.Groups.Count == 0
                ? LayoutStrategy.Flow
                : LayoutStrategy.Layered,
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
        strategy == LayoutStrategy.Manual;

    /// <summary>Runs the requested strategy. Manual is a no-op.</summary>
    public static LayoutResult Apply(
        Diagram diagram, LayoutOptions? options = null, LayoutStrategy? strategyOverride = null)
    {
        options ??= new LayoutOptions();

        // Membership reaches the model by two routes — a container's Members list and a node's
        // GroupId — and only some producers fill both. Every engine below reads Members, so a
        // diagram carrying membership only in GroupId would be laid out as if it had no containers.
        NormaliseMembership(diagram);

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
        DecorationLayout.AvoidNodes(diagram);

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
        // Layered stacks bands bottom-up — a vertical result. So LeftToRight is the one that needs
        // the transpose; the two names were swapped, and `leftToRight` produced an 11" x 23.3"
        // strip while `topToBottom` produced 23.3" x 11".
        // Which direction needs the transpose depends on what the engine already produces.
        //
        //   Layered, Grid, Tree   stack upward  -> vertical native, so LeftToRight transposes
        //   Flow                  walks its sequence left to right -> horizontal native, so
        //                         TopToBottom transposes
        //
        // A single rule for both turned a left-to-right flow into a column: measured, every node of
        // a five-step chain came back with the same x.
        bool nativeVertical = strategy is not (LayoutStrategy.Manual or LayoutStrategy.Flow);

        bool needsTranspose =
            (nativeVertical && options.Direction == LayoutDirection.LeftToRight) ||
            (!nativeVertical && options.Direction == LayoutDirection.TopToBottom);

        if (needsTranspose && strategy != LayoutStrategy.Manual)
        {
            DiagramLayout.Transpose(diagram);

            // The swap turns "grows upward" into "grows leftward", so the reading order reverses:
            // measured, web landed at x=21.82 and bi at x=2.27 for an IR that reads web -> ... -> bi.
            // Reflecting restores left-to-right. This lives here rather than inside Transpose
            // because a reflection composed with the swap is no longer its own inverse.
            if (nativeVertical) DiagramLayout.MirrorHorizontally(diagram);
            else DiagramLayout.MirrorVertically(diagram);

            // Transposing swaps the page's own dimensions, so the decorations clamped inside the
            // loop above were clamped against the old sheet. Re-clamp them against the new one, or
            // a title drifts outside the page it has just been rotated into.
            foreach (var page in diagram.Pages) ClampDecorationsToPage(page);

            // Transposing swaps every box's width and height, but the text inside was measured
            // against the pre-transpose shape: a wide label fitted a wide box, and after the swap
            // the same box is narrow and the label overflows. Re-derive the boxes so sizing follows
            // the orientation the caller asked for.
            DiagramSizer.Apply(diagram);
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
    private static void ClampDecorationsToPage(Page page) =>
        // Delegated: clamping each decoration on its own collapsed the spacing between blocks that
        // overflowed the same edge. DecorationLayout moves them as a group instead.
        DecorationLayout.ClampToPage(new Diagram { Pages = { page } });

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

    /// <summary>
    /// Brings <see cref="Group.Members"/> and <see cref="Node.GroupId"/> into agreement before layout.
    ///
    /// The model declares that Members is "kept in sync with Node.GroupId", but nothing performs that
    /// sync, so the two carriers diverge by origin: benchmark JSON names only `group_id`, while the
    /// IR converter fills both. Every layout engine reads Members, so a diagram whose membership
    /// lives only in `group_id` looks like it has no containers at all — measured on
    /// benchmarks/workflow/medium, members 0,0,0 alongside three populated groupIds.
    ///
    /// Filling Members from GroupId is the safer direction: GroupId already participates in the
    /// incremental editor, nesting bridges and serialisation, and it is the field authors write.
    /// Idempotent, so calling it on a model that already agrees changes nothing.
    /// </summary>
    public static int NormaliseMembership(Diagram diagram)
    {
        int added = 0;

        foreach (var page in diagram.Pages)
        {
            var byGroup = page.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);

            foreach (var node in page.Nodes)
            {
                if (node.GroupId is not { } groupId) continue;
                if (!byGroup.TryGetValue(groupId, out var group)) continue;
                if (group.Members.Contains(node.Id, StringComparer.Ordinal)) continue;

                group.Members.Add(node.Id);
                added++;
            }
        }

        return added;
    }
}
