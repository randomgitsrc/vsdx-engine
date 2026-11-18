using System.Text.Json;
using System.Text.Json.Serialization;
using Vsdx.Engine;
using Vsdx.Model;

namespace Vsdx.Protocol;

/// <summary>
/// Design Spec — *how* a diagram should be expressed: layout intent, visual theme, constraints.
///
/// Phase 7 §7.1 asks DesignSpec to describe intent and constraints without locking itself to one
/// diagram type, and without requiring the caller to precompute geometry. Every field here is a
/// *choice*, not a coordinate.
/// </summary>
public sealed class DesignSpec
{
    public string SpecVersion { get; set; } = ProtocolVersion.DesignSpec;

    /// <summary>Layout arrangement: layered | flow | tree | grid | auto | manual.</summary>
    public string Layout { get; set; } = "auto";

    /// <summary>Primary reading direction: leftToRight | topToBottom.</summary>
    public string Direction { get; set; } = "leftToRight";

    /// <summary>Spacing intent. Values are hints; the engine keeps its own minimums.</summary>
    public SpacingSpec Spacing { get; set; } = new();

    /// <summary>
    /// Canvas intent. "auto" fits the page to the content; a fixed size is honoured when the
    /// caller needs a specific paper format.
    /// </summary>
    public CanvasSpec Canvas { get; set; } = new();

    public TypographySpec Typography { get; set; } = new();

    /// <summary>Per-container strategy overrides, keyed by container id.</summary>
    public Dictionary<string, string>? ContainerLayouts { get; set; }

    /// <summary>Hard constraints the engine must satisfy or report as unmet.</summary>
    public List<ConstraintSpec> HardConstraints { get; set; } = new();

    /// <summary>Soft preferences; the engine optimises but may not satisfy.</summary>
    public List<ConstraintSpec> SoftConstraints { get; set; } = new();

    public ThemeSpec? Theme { get; set; }
}

public sealed class SpacingSpec
{
    public double? NodeGapX { get; set; }
    public double? BandGapY { get; set; }
}

public sealed class CanvasSpec
{
    /// <summary>"auto" or "fixed".</summary>
    public string Mode { get; set; } = "auto";

    public double? Width { get; set; }
    public double? Height { get; set; }

    /// <summary>inches | cm | mm.</summary>
    public string Unit { get; set; } = "inches";

    /// <summary>Shrink the page when the content is smaller than the requested size.</summary>
    public bool ShrinkToFit { get; set; } = true;
}

public sealed class TypographySpec
{
    public string? FontFamily { get; set; }

    /// <summary>Base node-title size in points. Omitted means derive from canvas and density.</summary>
    public double? BaseSize { get; set; }

    /// <summary>Never go below this size; density must be solved by splitting, not shrinking.</summary>
    public double MinReadableSize { get; set; } = TypeScale.MinReadablePt;
}

public sealed class ThemeSpec
{
    /// <summary>Named palette: enterprise | office | technical | muted.</summary>
    public string? Palette { get; set; }

    public string? Background { get; set; }

    /// <summary>Fill colour per semantic role, keyed by role name.</summary>
    public Dictionary<string, string>? RoleColors { get; set; }
}

public sealed class ConstraintSpec
{
    /// <summary>
    /// Constraint code: max_nodes_per_band | min_page_utilisation | max_edge_detour |
    /// no_edge_crossing | require_title | max_page_width …
    /// </summary>
    public string Code { get; set; } = "";

    /// <summary>Numeric threshold where applicable.</summary>
    public double? Value { get; set; }

    /// <summary>Target object for object-scoped constraints (node/container id).</summary>
    public string? Target { get; set; }

    public string? Note { get; set; }
}

/// <summary>
/// Applies a DesignSpec to a model and reports every constraint it could not satisfy.
///
/// The contract is: the engine never silently ignores a declared constraint. Each one is either
/// satisfied, or reported in the execution result with a machine-readable code.
/// </summary>
public static class DesignSpecApplier
{
    /// <summary>Runs the spec against a diagram, returning what was applied and what was not.</summary>
    public static DesignApplication Apply(Diagram diagram, DesignSpec spec)
    {
        var applied = new List<string>();
        var unmet = new List<UnmetConstraint>();

        // --- Typography ----------------------------------------------------
        if (spec.Typography.BaseSize is not null || spec.Typography.FontFamily is not null)
        {
            var family = spec.Typography.FontFamily;
            var baseSize = spec.Typography.BaseSize;

            // BaseSize names the target size of a node title, not one size for everything. The
            // remaining tiers keep their proportions by scaling from NodeTitle, so raising it makes
            // the whole diagram read bigger without flattening the hierarchy into one size.
            var defaults = new TypeScale();
            var reportedClamps = new HashSet<double>();
            double requested = baseSize ?? defaults.NodeTitle;
            double scale = baseSize is null ? 1.0 : requested / defaults.NodeTitle;

            double Tier(double basePt)
            {
                if (baseSize is null) return basePt;
                double wanted = basePt * scale;
                double clamped = Math.Max(wanted, spec.Typography.MinReadableSize);

                // Once per tier, not once per object: the clamp is a property of the scale, and
                // repeating it for every node carrying that tier buries the message in duplicates.
                if (clamped > wanted && Math.Abs(clamped - wanted) > 0.01 && reportedClamps.Add(basePt))
                {
                    unmet.Add(new UnmetConstraint
                    {
                        Code = "typography.min_size",
                        Reason = $"{basePt:0.#}pt 档按 {requested:0.#}pt 基准缩放后为 {wanted:0.#}pt，" +
                                 $"低于可读下限 {spec.Typography.MinReadableSize:0.#}pt，已提升"
                    });
                }

                return clamped;
            }

            void ApplyFamily(ShapeStyle style)
            {
                if (family is not null) style.Font ??= family;
            }

            foreach (var page in diagram.Pages)
            {
                foreach (var node in page.Nodes)
                {
                    node.Style ??= new ShapeStyle();
                    if (baseSize is not null) node.Style.FontSize ??= Tier(defaults.NodeTitle);
                    ApplyFamily(node.Style);
                }

                // Container headings are a separate tier; leaving them at the writer's hardcoded
                // 12pt is what made a "14pt diagram" only 14pt inside the boxes.
                foreach (var group in page.Groups)
                {
                    group.Style ??= new ShapeStyle();
                    if (baseSize is not null) group.Style.FontSize ??= Tier(defaults.RegionTitle);
                    ApplyFamily(group.Style);
                }

                foreach (var edge in page.Edges)
                {
                    if (baseSize is not null && edge.LabelFontSize is null)
                        edge.LabelFontSize = Tier(defaults.EdgeLabel);
                    if (family is not null) edge.LabelFont ??= family;
                }

                foreach (var text in page.TextBlocks)
                {
                    text.Style ??= new ShapeStyle();
                    if (baseSize is not null)
                        text.Style.FontSize ??= Tier(DesignSpecApplier.TierForRole(text.Role, defaults));
                    ApplyFamily(text.Style);
                }
            }

            applied.Add("typography");
        }

        // --- Palette -------------------------------------------------------
        // Applied first, so later defaults fill only what it leaves unset. A palette is the
        // broadest layer: object style beats it, role colour beats it, and the band tints below
        // only supply values it does not define.
        Palettes.Definition? palette = null;
        if (!string.IsNullOrWhiteSpace(spec.Theme?.Palette))
        {
            if (Palettes.TryGet(spec.Theme.Palette, out var found))
            {
                palette = found;
                applied.Add($"theme.palette={found.Name}");

                foreach (var page in diagram.Pages)
                {
                    foreach (var node in page.Nodes)
                    {
                        // An explicit fill is the author's decision; the palette is only a default.
                        if (node.Style?.Fill is not null) continue;

                        var colour = node.Type is not null && found.Roles.TryGetValue(node.Type, out var role)
                            ? role
                            : found.Default;

                        node.Style ??= new ShapeStyle();
                        node.Style.Fill = colour;
                        if (node.Style.TextColor is null)
                            node.Style.TextColor = ColorUtil.ReadableTextColor(colour);
                    }
                }
            }
            else
            {
                // Silently accepting an unknown name would leave the caller believing the style
                // was applied, which is the failure mode this whole pass exists to remove.
                unmet.Add(new UnmetConstraint
                {
                    Code = "theme.palette-unknown",
                    Target = "theme.palette",
                    Reason = $"未知调色板 '{spec.Theme.Palette}'，可用：" +
                             string.Join(" / ", Palettes.Names.OrderBy(n => n, StringComparer.Ordinal))
                });
            }
        }

        // Sizing has to run here: after fonts are final, before placement. Without this stage the
        // spec path kept every node at the converter's placeholder box, so a 24pt request and an
        // 11pt request produced identical geometry — passing a spec was worse than passing none,
        // because the no-spec branch does size.
        DiagramSizer.Apply(diagram);
        applied.Add("sizing");

        // --- Layout --------------------------------------------------------
        bool isAuto = spec.Layout.Equals("auto", StringComparison.OrdinalIgnoreCase);
        LayoutStrategy strategy;
        if (isAuto)
        {
            strategy = DiagramLayoutEngine.SuggestStrategy(diagram);
        }
        else if (!DiagramLayoutEngine.TryParse(spec.Layout, out strategy))
        {
            // Running Layered after being asked for something unrecognised is a reasonable
            // fallback; doing it silently is not, because the caller cannot tell a typo from a
            // deliberate request.
            unmet.Add(new UnmetConstraint
            {
                Code = "layout.unknown",
                Target = "layout",
                Reason = $"未知布局 '{spec.Layout}'，已回退为 {strategy}"
            });
        }

        bool manualRequested = spec.Layout.Equals("manual", StringComparison.OrdinalIgnoreCase);

        bool wantsTopToBottom = spec.Direction.Equals("topToBottom", StringComparison.OrdinalIgnoreCase);
        bool wantsLeftToRight = spec.Direction.Equals("leftToRight", StringComparison.OrdinalIgnoreCase);
        if (!wantsTopToBottom && !wantsLeftToRight)
        {
            unmet.Add(new UnmetConstraint
            {
                Code = "direction.unknown",
                Target = "direction",
                Reason = $"未知方向 '{spec.Direction}'，已回退为 leftToRight"
            });
        }

        // Manual promises that the author's coordinates survive untouched, so a top-to-bottom
        // request cannot be honoured there. Saying so is the only honest option: silently
        // ignoring it would leave the caller believing the page was transposed.
        if (manualRequested && wantsTopToBottom)
        {
            unmet.Add(new UnmetConstraint
            {
                Code = "direction.manual-ignored",
                Target = "layout",
                Reason = "manual 保留作者坐标，direction=topToBottom 未应用"
            });
        }

        if (!string.IsNullOrWhiteSpace(spec.Layout) && !manualRequested)
        {
            var options = new LayoutOptions
            {
                NodeGapX = spec.Spacing.NodeGapX ?? 0.55,
                BandGapY = spec.Spacing.BandGapY ?? 0.85,
                Direction = wantsTopToBottom
                    ? LayoutDirection.TopToBottom
                    : LayoutDirection.LeftToRight
            };

            DiagramLayoutEngine.Apply(diagram, options, strategy);
            applied.Add($"layout={strategy}");

            // Per-container overrides are reported, not silently dropped.
            if (spec.ContainerLayouts is { Count: > 0 })
            {
                foreach (var (containerId, requested) in spec.ContainerLayouts)
                {
                    // Parse before applying: claiming "relaid as kaboom" was the worst of the four
                    // silent fallbacks, because it asserted a strategy that does not exist.
                    bool known = DiagramLayoutEngine.TryParse(requested, out var containerStrategy);
                    if (!known)
                    {
                        unmet.Add(new UnmetConstraint
                        {
                            Code = "container_layout.unknown",
                            Target = containerId,
                            Reason = $"未知容器布局 '{requested}'，已跳过该容器"
                        });
                        continue;
                    }

                    var result = LocalRelayout.Apply(diagram, containerId, containerStrategy);

                    if (result.Ok) applied.Add($"container {containerId} relaid as {requested}");
                    else
                        unmet.Add(new UnmetConstraint
                        {
                            Code = "container_layout",
                            Target = containerId,
                            Reason = result.Note ?? "无法应用容器级布局"
                        });
                }
            }
        }
        else
        {
            applied.Add("layout=manual");

            // Manual promises the author's coordinates survive, so per-container layout cannot be
            // honoured here. The request used to be dropped inside the branch above without a
            // word, which reads as "applied" to anyone checking only `unmet`.
            if (spec.ContainerLayouts is { Count: > 0 })
            {
                foreach (var containerId in spec.ContainerLayouts.Keys)
                {
                    unmet.Add(new UnmetConstraint
                    {
                        Code = "container_layout.manual",
                        Target = containerId,
                        Reason = $"layout=manual 保留作者坐标，容器 '{containerId}' 的布局请求未应用"
                    });
                }
            }
        }

        DiagramLayout.Apply(diagram);

        // --- Container styling ---------------------------------------------
        // Containers previously inherited the writer's default fill, so every band rendered in
        // the same pale blue and the diagram read as one undifferentiated block. Bands get a
        // restrained tint from the palette instead, and lanes get a distinctly lighter one so
        // the two structures stay distinguishable.
        // Fallback tints for diagrams without a palette. These existed before palettes did, so
        // containers would not be left on the writer's default grey; that job is now shared with
        // the palette, which supplies its own sequence when one is named.
        var fallbackBands = new[]
        {
            new Palettes.BandColour("#EAF3FB", "#9DC3E6", "#1F4E79"),
            new Palettes.BandColour("#EDF7ED", "#A9D08E", "#375623"),
            new Palettes.BandColour("#FDF2E9", "#F4B183", "#833C0B"),
            new Palettes.BandColour("#F2E9F7", "#C5A3D9", "#4B1F6F"),
            new Palettes.BandColour("#EAF6F6", "#93CCCC", "#1F5C5C")
        };

        var bands = palette?.Bands is { Count: > 0 } paletteBands ? paletteBands : fallbackBands;

        foreach (var page in diagram.Pages)
        {
            int index = 0;
            foreach (var group in page.Groups)
            {
                var band = bands[index % bands.Count];
                group.Style ??= new ShapeStyle();
                group.Style.Fill ??= band.Fill;
                group.Style.Stroke ??= band.Stroke;
                group.Style.TextColor ??= band.Text;
                group.Style.Bold ??= true;
                group.Style.HAlign ??= "left";
                index++;
            }
        }
        applied.Add("container.bands");

        // --- Theme ---------------------------------------------------------
        if (spec.Theme?.RoleColors is { Count: > 0 })
        {
            // `default` is a fallback for roles the map does not name, not a role itself — so it
            // must not be looked up as one, or a diagram with a `default` role would shadow it.
            spec.Theme.RoleColors.TryGetValue("default", out var fallbackColour);

            // One complaint per role, however many nodes carry it: the problem is the map, not the
            // nodes, and repeating it per node turns a signal into noise.
            var uncovered = new SortedSet<string>(StringComparer.Ordinal);
            int coloured = 0;

            foreach (var page in diagram.Pages)
            {
                foreach (var node in page.Nodes)
                {
                    if (node.Type is null) continue;

                    string? colour = null;
                    if (spec.Theme.RoleColors.TryGetValue(node.Type, out var explicitColour))
                        colour = explicitColour;
                    else if (fallbackColour is not null)
                        colour = fallbackColour;
                    else
                    {
                        // Skipping silently left the caller with a successful run and an uncoloured
                        // node — the misspelt-key case. Naming the role is the whole fix.
                        uncovered.Add(node.Type);
                        continue;
                    }

                    node.Style ??= new ShapeStyle();
                    node.Style.Fill = colour;
                    node.Style.TextColor = ColorUtil.ReadableTextColor(colour);
                    coloured++;
                }
            }

            if (coloured > 0) applied.Add("theme.roleColors");

            foreach (var role in uncovered)
            {
                unmet.Add(new UnmetConstraint
                {
                    Code = "theme.role-uncovered",
                    Target = role,
                    Reason = $"角色 '{role}' 不在 roleColors 中，且未提供 default 回退，该角色未被着色"
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(spec.Theme?.Background))
        {
            diagram.Background = spec.Theme.Background;
            foreach (var page in diagram.Pages) page.Background ??= spec.Theme.Background;
            applied.Add("theme.background");
        }

        // --- Canvas --------------------------------------------------------
        foreach (var page in diagram.Pages)
        {
            if (spec.Canvas.Mode.Equals("fixed", StringComparison.OrdinalIgnoreCase) &&
                spec.Canvas.Width is double w && spec.Canvas.Height is double h)
            {
                page.Size ??= new PageSize();
                page.Size.Width = w;
                page.Size.Height = h;
                page.Size.Unit = spec.Canvas.Unit;

                string target = $"{w:0.##}x{h:0.##}";

                if (spec.Canvas.ShrinkToFit)
                {
                    var fit = DiagramSizer.FitPage(page, shrink: true);

                    // Trimming a page the caller asked for is not "applied" — it is the request
                    // being overridden to make the drawing fit. Saying so in `applied` alone hides
                    // it: nobody checks the notes for the requirements that were dropped.
                    if (fit.Shrank)
                    {
                        applied.Add("canvas.fixed+shrunk");
                        unmet.Add(new UnmetConstraint
                        {
                            Code = "canvas.shrunk",
                            Target = page.Name,
                            Reason = $"请求 {target} 英寸，为容纳内容收缩为 " +
                                     $"{page.Size.Width:0.##}x{page.Size.Height:0.##} 英寸"
                        });
                    }
                    else
                    {
                        applied.Add("canvas.fixed");
                    }
                }
                else
                {
                    applied.Add("canvas.fixed");
                }

                // The other direction matters too: content larger than the requested sheet grows
                // it, which is equally a size the caller did not ask for.
                double overW = page.Size.Width - w;
                double overH = page.Size.Height - h;
                if (overW > 0.05 || overH > 0.05)
                {
                    unmet.Add(new UnmetConstraint
                    {
                        Code = "canvas.exceeded",
                        Target = page.Name,
                        Reason = $"请求 {target} 英寸，内容超出，实际为 " +
                                 $"{page.Size.Width:0.##}x{page.Size.Height:0.##} 英寸"
                    });
                }
            }
            else
            {
                DiagramSizer.Recenter(page);
                DiagramSizer.FitPage(page, shrink: true);
                applied.Add("canvas.auto");

                // A fixed size cannot be honoured together with auto — report the conflict.
                if (spec.Canvas.Width is not null || spec.Canvas.Height is not null)
                {
                    unmet.Add(new UnmetConstraint
                    {
                        Code = "canvas.mode",
                        Reason = "canvas.mode=auto 时 width/height 被忽略"
                    });
                }
            }
        }

        // --- Constraints ---------------------------------------------------
        var report = LayoutAnalyzer.Analyze(diagram);
        foreach (var constraint in spec.HardConstraints)
            unmet.AddRange(Evaluate(constraint, diagram, report, isHard: true));
        foreach (var constraint in spec.SoftConstraints)
            unmet.AddRange(Evaluate(constraint, diagram, report, isHard: false));

        return new DesignApplication
        {
            Applied = applied,
            Unmet = unmet,
            Strategy = strategy,
            Report = report
        };
    }

    /// <summary>Checks one constraint against the current model and report.</summary>
    private static IEnumerable<UnmetConstraint> Evaluate(
        ConstraintSpec constraint, Diagram diagram, LayoutReport report, bool isHard)
    {
        bool satisfied;
        string detail;

        switch (constraint.Code)
        {
            case "min_page_utilisation":
            {
                double threshold = constraint.Value ?? 0.30;
                var ratios = report.PageUtilisation.Values;
                satisfied = ratios.Any() && ratios.All(r => r >= threshold);
                detail = $"页面利用率 {string.Join(", ", ratios.Select(r => r.ToString("P0")))} < {threshold:P0}";
                break;
            }

            case "max_edge_detour":
            {
                double limit = constraint.Value ?? 2.0;
                var over = report.EdgeDetourRatio.Where(kv => kv.Value > limit).ToList();
                satisfied = over.Count == 0;
                detail = $"超限连线：{string.Join(", ", over.Select(kv => $"{kv.Key}={kv.Value:F2}"))}";
                break;
            }

            case "no_edge_crossing":
            {
                var crossings = report.Issues.Where(i => i.Code == "edge.crossing").ToList();
                satisfied = crossings.Count == 0;
                detail = $"{crossings.Count} 处交叉";
                break;
            }

            case "max_nodes_per_band":
            {
                int limit = (int)(constraint.Value ?? 6);
                var over = diagram.Pages
                    .SelectMany(p => p.Groups.Select(g => (g.Id, g.Members.Count)))
                    .Where(t => t.Count > limit)
                    .ToList();
                satisfied = over.Count == 0;
                detail = $"超出：{string.Join(", ", over.Select(t => $"{t.Id}={t.Count}"))}";
                break;
            }

            case "require_title":
            {
                satisfied = !string.IsNullOrWhiteSpace(diagram.Name);
                detail = "缺少图表标题";
                break;
            }

            case "max_page_width":
            {
                double limit = constraint.Value ?? 22;
                var over = diagram.Pages.Where(p => (p.Size?.Width ?? 0) > limit).ToList();
                satisfied = over.Count == 0;
                detail = $"页面宽度超出 {limit}\"：{string.Join(", ", over.Select(p => p.Name))}";
                break;
            }

            case "no_hard_violations":
            {
                satisfied = report.HardConstraintsPassed;
                detail = string.Join(", ", report.Errors.Select(e => e.Code).Distinct());
                break;
            }

            default:
                return new[]
                {
                    new UnmetConstraint
                    {
                        Code = "unknown_constraint",
                        Target = constraint.Target,
                        Reason = $"引擎不认识约束 '{constraint.Code}'，未执行",
                        IsHard = isHard
                    }
                };
        }

        if (satisfied) return Array.Empty<UnmetConstraint>();

        return new[]
        {
            new UnmetConstraint
            {
                Code = constraint.Code,
                Target = constraint.Target,
                Reason = detail,
                IsHard = isHard
            }
        };
    }

    /// <summary>
    /// Maps a text block's semantic role to its place in the type scale.
    ///
    /// Mirrors the mapping the renderer uses; a caption should not be sized like a diagram title
    /// just because both happen to be text blocks.
    /// </summary>
    private static double TierForRole(string? role, TypeScale scale) => role?.ToLowerInvariant() switch
    {
        "title" => scale.DiagramTitle,
        "subtitle" => scale.RegionTitle,
        "caption" => scale.NodeCaption,
        "legend" => scale.NodeCaption,
        "note" => scale.Annotation,
        _ => scale.NodeCaption
    };

}

public sealed class DesignApplication
{
    public required IReadOnlyList<string> Applied { get; init; }
    public required IReadOnlyList<UnmetConstraint> Unmet { get; init; }
    public required LayoutStrategy Strategy { get; init; }
    public required LayoutReport Report { get; init; }

    public bool AllHardConstraintsMet => !Unmet.Any(u => u.IsHard);
}

public sealed class UnmetConstraint
{
    public string Code { get; set; } = "";
    public string? Target { get; set; }
    public string Reason { get; set; } = "";
    public bool IsHard { get; set; }
}
