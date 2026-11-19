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
    /// <summary>
    /// Property names present in the JSON but not mapped to any field.
    ///
    /// Populated by the loader, never serialised. `System.Text.Json` ignores unmapped properties by default, so
    /// without this a misspelled or unsupported field — `canvas.profile`, say — deserialises cleanly and the
    /// request evaporates while the run reports success. Goal §5.3 forbids that: a request the engine cannot
    /// honour has to be reported, not dropped.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> UnknownFields { get; } = new();

    public string SpecVersion { get; set; } = ProtocolVersion.DesignSpec;

    /// <summary>Layout arrangement: layered | flow | tree | grid | auto | manual.</summary>
    public string Layout { get; set; } = "auto";

    /// <summary>Primary reading direction: leftToRight | topToBottom.</summary>
    /// <summary>
    /// Reading direction. Null means "pick one that suits the structure".
    ///
    /// It used to default to the string "leftToRight", which made an unstated direction
    /// indistinguishable from a stated one — so once LeftToRight began to transpose (C15), every
    /// diagram that omitted the field silently rotated. Layered structures lost 6-18 points that
    /// way; a container-less flow gained 3.
    /// </summary>
    public string? Direction { get; set; }

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

    /// <summary>
    /// Give every band the same width. Default true: a layered drawing whose bands end wherever their own members
    /// do reads as assembled rather than designed. Set false for content-sized bands.
    /// </summary>
    public bool? EvenBands { get; set; }
}

public sealed class CanvasSpec
{
    /// <summary>
    /// "auto" fits the page to the content, "fixed" honours width/height, "paper" snaps to the
    /// smallest standard sheet that holds the content.
    /// </summary>
    public string Mode { get; set; } = "auto";

    public double? Width { get; set; }
    public double? Height { get; set; }

    /// <summary>inches | cm | mm.</summary>
    public string Unit { get; set; } = "inches";

    /// <summary>Shrink the page when the content is smaller than the requested size.</summary>
    public bool ShrinkToFit { get; set; } = true;

    /// <summary>
    /// Draw a legend describing the roles the diagram uses. **On by default.**
    ///
    /// It used to be opt-in, on the reasoning that a legend adds objects and a caller who laid out a tight drawing
    /// should get exactly that. What that produced in practice was worse: diagrams with five colours, four shapes
    /// and three bands, and no key — or a hand-written one that had drifted. The shipped `deploy-topology` example
    /// carried "蓝=入口" while both of its entry-zone nodes are `#F2B705` gold. A false legend is worse than none,
    /// and a derived one cannot drift: the labels come from the roles the drawing actually contains.
    ///
    /// Set `"canvas": {"legend": false}` to omit it.
    /// </summary>
    public bool Legend { get; set; } = true;
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
    /// <summary>
    /// Named palette: enterprise | muted | technical | midnight. An unknown name is reported as
    /// `theme.palette-unknown` rather than silently ignored.
    /// </summary>
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
/// <summary>Finds fields present in the JSON that no `DesignSpec` type maps to.</summary>
public static class DesignSpecLoader
{
    /// <summary>
    /// Reads a spec and records every property name the model does not have.
    ///
    /// Only names are reported, not values — a caller who wrote `canvas.profile` needs to know the field was
    /// ignored, and the value is already in their file.
    /// </summary>
    public static DesignSpec Load(string json)
    {
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(json, options)
                   ?? new DesignSpec();

        Collect(System.Text.Json.JsonDocument.Parse(json).RootElement, typeof(DesignSpec), "", spec.UnknownFields);

        return spec;
    }

    private static void Collect(
        System.Text.Json.JsonElement element, Type type, string path, List<string> unknown)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return;

        var properties = type.GetProperties()
            .Where(p => p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false).Length == 0)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            var full = path.Length == 0 ? name : $"{path}.{name}";

            if (!properties.TryGetValue(name, out var matched))
            {
                unknown.Add(full);
                continue;
            }

            // Recurse into nested objects only; arrays of scalars carry no field names worth checking.
            Collect(property.Value, matched.PropertyType, full, unknown);
        }
    }
}

public static class DesignSpecApplier
{
    /// <summary>Runs the spec against a diagram, returning what was applied and what was not.</summary>
    public static DesignApplication Apply(Diagram diagram, DesignSpec spec)
    {
        var applied = new List<string>();
        var unmet = new List<UnmetConstraint>();

        // The IR has been version-checked since the beginning; the spec was not, so `specVersion: 9.9`
        // was accepted and echoed back as though understood. Unknown *fields* may be ignored, but an
        // unknown major version means the document describes something this build does not know, and
        // silently applying what it happens to recognise is the worst of both.
        if (!ProtocolVersion.IsCompatible(spec.SpecVersion, "1"))
        {
            unmet.Add(new UnmetConstraint
            {
                Code = "spec.version",
                Target = "specVersion",
                Reason = $"不支持的 DesignSpec 版本 '{spec.SpecVersion}'，当前支持 1.x"
            });

            // Nothing was applied, so the report describes the drawing as it stands.
            return new DesignApplication
            {
                Applied = applied,
                Unmet = unmet,
                Strategy = DiagramLayoutEngine.SuggestStrategy(diagram),
                Report = LayoutAnalyzer.Analyze(diagram)
            };
        }

        // --- Typography ----------------------------------------------------
        // `minReadableSize` alone is a valid request: the caller may keep the default scale and only
        // raise the floor. Requiring baseSize or a font family made that a silent no-op — measured,
        // `{"typography":{"minReadableSize":20}}` returned succeeded with `unmet: []` and left every
        // size at its default.
        if (spec.Typography.BaseSize is not null ||
            spec.Typography.FontFamily is not null ||
            spec.Typography.MinReadableSize > TypeScale.MinReadablePt)
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
                // The floor applies even without a base size: a caller who asks for 20pt minimum and
                // leaves the scale alone still means it.
                double wanted = baseSize is null ? basePt : basePt * scale;
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
                // A page's type scale follows its own density, so decorations must use the same scale the nodes
                // do. They used to take their size from a bare `new TypeScale()`, whose `NodeCaption` is a fixed
                // 10.0 no matter how packed the drawing is — measured on `netflix-active-active`, the legend came
                // out at 10.0pt while node titles were 8.4pt, so the key was larger than what it keyed.
                var pageScale = TypeScale.ForCanvas(
                    page.Size?.Width ?? 11, page.Size?.Height ?? 8.5, page.Nodes.Count);

                foreach (var node in page.Nodes)
                {
                    node.Style ??= new ShapeStyle();
                    // Always go through Tier: it returns the tier size unchanged when no base size
                    // was given, and raises it to the floor when one was. Guarding on baseSize here
                    // meant a floor-only request set no size at all.
                    node.Style.FontSize ??= Tier(pageScale.NodeTitle);
                    ApplyFamily(node.Style);
                }

                // Container headings are a separate tier; leaving them at the writer's hardcoded
                // 12pt is what made a "14pt diagram" only 14pt inside the boxes.
                foreach (var group in page.Groups)
                {
                    group.Style ??= new ShapeStyle();
                    group.Style.FontSize ??= Tier(defaults.RegionTitle);
                    ApplyFamily(group.Style);
                }

                foreach (var edge in page.Edges)
                {
                    if (edge.LabelFontSize is null)
                        edge.LabelFontSize = Tier(defaults.EdgeLabel);
                    if (family is not null) edge.LabelFont ??= family;
                }

                foreach (var text in page.TextBlocks)
                {
                    text.Style ??= new ShapeStyle();
                    // Unconditional, like nodes and containers: Tier returns the tier size when no
                    // base size was given and raises it to the floor when one was. Measured with
                    // `minReadableSize: 30`, a note stayed at the bridge's hardcoded 8pt.
                    text.Style.FontSize ??= Tier(DesignSpecApplier.TierForRole(text.Role, pageScale));
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

        // Before any page sizing: a heading box wider than its text makes `FitPage` grow the sheet around blank
        // paper. `cbo` authored 6.00" for eight characters and the page came out 11.00" with 4.41" empty.
        if (DiagramSizer.FitTextBlocks(diagram) > 0)
            applied.Add("text.fit");
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

        bool directionStated = !string.IsNullOrWhiteSpace(spec.Direction);
        bool wantsTopToBottom =
            string.Equals(spec.Direction, "topToBottom", StringComparison.OrdinalIgnoreCase);
        bool wantsLeftToRight =
            string.Equals(spec.Direction, "leftToRight", StringComparison.OrdinalIgnoreCase);
        if (directionStated && !wantsTopToBottom && !wantsLeftToRight)
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
        if (manualRequested && directionStated)
        {
            unmet.Add(new UnmetConstraint
            {
                Code = "direction.manual-ignored",
                Target = "layout",
                Reason = $"manual 保留作者坐标，direction={spec.Direction} 未应用"
            });
        }

        if (!string.IsNullOrWhiteSpace(spec.Layout) && !manualRequested)
        {
            var options = new LayoutOptions
            {
                NodeGapX = spec.Spacing.NodeGapX ?? 0.55,
                BandGapY = spec.Spacing.BandGapY ?? 0.85,

                // Layers in a professional drawing are flush. A band sized by its own members turns the right
                // edge into a staircase, which is the single strongest "assembled rather than designed" signal in
                // the reference diagrams.
                EvenBands = spec.Spacing.EvenBands ?? true,
                // Unstated: layered structures read top to bottom, because bands are a vertical
                // idea and turning them sideways spreads each one across the page. A container-less
                // flow has no bands to stack, so it reads left to right.
                Direction = wantsTopToBottom
                    ? LayoutDirection.TopToBottom
                    : wantsLeftToRight
                        ? LayoutDirection.LeftToRight
                        : diagram.Pages.Any(p => p.Groups.Count > 0)
                            ? LayoutDirection.TopToBottom
                            : LayoutDirection.LeftToRight
            };

            DiagramLayoutEngine.Apply(diagram, options, strategy);
            // Report the direction actually used, not just that one was inferred. "direction=auto"
            // leaves a reader unable to tell whether their diagram came out portrait or landscape
            // without opening the file.
            string chosen = options.Direction == LayoutDirection.TopToBottom ? "topToBottom" : "leftToRight";

            applied.Add(directionStated
                ? $"layout={strategy}, direction={chosen}"
                : $"layout={strategy}, direction={chosen} (auto)");

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

            // Spacing has the same problem: it is only consumed by the layout engines, so under
            // manual it cannot take effect. `direction` and `containerLayouts` already report from
            // this branch; spacing was the one silently dropped.
            if (spec.Spacing.NodeGapX is not null || spec.Spacing.BandGapY is not null)
            {
                unmet.Add(new UnmetConstraint
                {
                    Code = "spacing.manual-ignored",
                    Target = "layout",
                    Reason = "manual 保留作者坐标，spacing 未应用"
                });
            }
        }

        DiagramLayout.Apply(diagram);

        // Container bounds are final only after the pass above — it derives them from members. A
        // band can therefore grow past a title that was clear when the layout engines ran, so the
        // avoidance has to run once more here. The earlier call inside the layout pipeline handles
        // the no-spec path, where this pass is not reached.
        if (DecorationLayout.AvoidContainers(diagram) > 0)
            applied.Add("decoration.clearance");

        // A text block that lands on a node is drawn over it, and nothing else in the pipeline moves it.
        if (DecorationLayout.AvoidNodes(diagram) > 0)
            applied.Add("decoration.node-clearance");

        // Labels that land on each other are separated here rather than left for the caller to fix
        // by hand. Only labels without an explicit position move; the result is re-checked, so this
        // either helps or does nothing.
        if (DecorationLayout.SpreadLabels(diagram) > 0)
            applied.Add("label.spread");

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
        // Only claim the step when it did something: a diagram with no containers had no bands to
        // style, and recording the note anyway made `applied` describe work that never happened.
        if (diagram.Pages.Any(p => p.Groups.Count > 0)) applied.Add("container.bands");

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

            if (coloured > 0)
            {
                applied.Add("theme.roleColors");
            }
            else if (uncovered.Count == 0)
            {
                // A map was supplied but no node carries a role, so nothing could be coloured. That
                // is neither applied nor an uncovered role, which left it invisible: the caller saw
                // no mention of their request at all.
                unmet.Add(new UnmetConstraint
                {
                    Code = "theme.roleColors-unused",
                    Target = "theme",
                    Reason = "提供了 roleColors，但图中没有带 role 的节点，未着色任何对象"
                });
            }

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
        // `paper` picks the smallest standard sheet that holds the content, in whichever orientation
        // fits better. Auto produced custom sizes — 8.55x9.68, 11.00x11.85, 15.57x10.45 on the three
        // real scenarios — none of which a print dialog knows what to do with.
        if (spec.Canvas.Mode.Equals("paper", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var page in diagram.Pages)
            {
                var sheet = DiagramSizer.ChoosePaper(page, out bool scaledDown);

                page.Size ??= new PageSize();
                page.Size.Width = sheet.Width;
                page.Size.Height = sheet.Height;
                page.Size.Unit = "inches";

                // Centre the content on the chosen sheet. Without this the nodes keep the
                // coordinates the layout produced — which were measured against the old page — so a
                // 4.25" tall diagram landed at the bottom of a 16.54" sheet with 8.11" of empty
                // space above. The paper was correct; the placement was not.
                DecorationLayout.CenterContent(page);

                applied.Add($"canvas.paper={sheet.Name}");

                if (scaledDown)
                {
                    // `scaledDown` means only "no standard sheet was large enough, so the largest was
                    // used". Nothing shrinks the drawing — the previous wording said the content had
                    // been scaled to fit, which was untrue: measured, a 73" chain on a 17" Tabloid
                    // left 32 nodes outside the page while the report claimed it had been fitted.
                    unmet.Add(new UnmetConstraint
                    {
                        Code = "canvas.scaled",
                        Target = page.Name,
                        Reason = $"内容超出最大的标准纸张 {sheet.Name}（{sheet.Width:0.##}x{sheet.Height:0.##} 英寸）"
                                 + "，未缩小内容；超出的部分在页面之外"
                    });
                }

                // Whether or not a standard sheet was chosen, check what actually landed on it.
                // `paper` mode sized the page and verified nothing: measured on a 20-node chain, 12 of
                // 20 shapes sat outside the sheet and the result was still `partial` with no
                // `canvas.exceeded` — a file whose shapes are off the page is not a deliverable.
                int outsideCount = CountOutsidePage(page);

                if (outsideCount > 0)
                {
                    unmet.Add(new UnmetConstraint
                    {
                        Code = "canvas.exceeded",
                        Target = page.Name,
                        // Hard: a file whose shapes sit outside the page cannot be delivered as-is.
                        // Reporting it as a soft note let the status stay `partial`.
                        IsHard = true,
                        Reason = $"{outsideCount} 个对象位于页面之外"
                                 + $"（页面 {page.Size.Width:0.##}x{page.Size.Height:0.##} 英寸）"
                    });
                }
            }
        }

        foreach (var page in diagram.Pages)
        {
            if (spec.Canvas.Mode.Equals("fixed", StringComparison.OrdinalIgnoreCase) &&
                spec.Canvas.Width is double w && spec.Canvas.Height is double h)
            {
                // Normalise to inches immediately. The value used to be stored with the caller's
                // unit still attached, so FitPage compared and rewrote it as inches while the unit
                // label said cm — and the writer then converted again on the way out. Measured:
                // a 10x10 cm request produced a 2.92x1.18 inch page, exactly 2.54x too small, with
                // every node pushed off the sheet.
                double unitFactor = Vsdx.Adapters.MasterCatalog.UnitFactor(spec.Canvas.Unit);
                double widthIn = w * unitFactor;
                double heightIn = h * unitFactor;

                page.Size ??= new PageSize();
                page.Size.Width = widthIn;
                page.Size.Height = heightIn;
                page.Size.Unit = "inches";

                string target = $"{widthIn:0.##}x{heightIn:0.##}";

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
                double overW = page.Size.Width - widthIn;
                double overH = page.Size.Height - heightIn;
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
            else if (spec.Canvas.Mode.Equals("paper", StringComparison.OrdinalIgnoreCase))
            {
                // Already sized to the chosen sheet above. Running the auto path here would re-fit
                // the page to the content and throw that sheet away — measured, a paper=A3 request
                // came out 12.80x3.00 with both `canvas.paper=A3` and `canvas.auto` in the notes.
            }
            else
            {
                // Manual promises the author's coordinates survive. Recentring shifts the page
                // contents without knowing who placed them: measured, nodes authored at (2,9), (6,9)
                // and (10,4) came back at (1.2,5.8), (5.2,5.8) and (9.2,0.8) — a uniform shift —
                // while `applied` only said `canvas.auto`.
                //
                // The page may still grow so the content stays on the sheet; what it may not do is
                // move the content.
                if (manualRequested)
                {
                    DiagramSizer.FitPage(page, shrink: false);
                    applied.Add("canvas.auto (manual: coordinates kept)");
                }
                else
                {
                    DiagramSizer.Recenter(page);
                    DiagramSizer.FitPage(page, shrink: true);
                    applied.Add("canvas.auto");
                }

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

        // The legend is drawn here, after every path that can change the page size, so its type follows the page
        // it will actually sit on. Built earlier it was sized against the pre-shrink page and came out larger than
        // the node labels: measured on `netflix-active-active`, legend 10.0pt against node title 8.4pt.
        //
        // Its position does not depend on the page — `LegendLayout` places rows below the lowest drawn element —
        // so moving it later changes only the type scale.
        if (spec.Canvas.Legend)
        {
            int rows = LegendLayout.Apply(diagram, spec.Theme?.Palette);
            if (rows > 0) applied.Add($"legend.rows={rows}");
        }

        // Headings are re-centred here, after every path that can change the page size. Doing it earlier would
        // centre against a page that is about to shrink.
        if (DecorationLayout.CentreTitles(diagram) > 0)
            applied.Add("title.centred");

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
                // A title must be visible on the page, not merely present as a name. `Diagram.Name`
                // is filled from `ir.Id` when no title is given, so it is never empty — measured, an
                // IR with no title satisfied this constraint while its page carried no text at all.
                bool hasTextTitle = diagram.Pages.Any(p => p.TextBlocks.Any(t =>
                    !string.IsNullOrWhiteSpace(t.Text) &&
                    (string.Equals(t.Role, "title", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(t.Role, "subtitle", StringComparison.OrdinalIgnoreCase))));

                // Only a visible block counts. `Diagram.Name` cannot: it falls back to `ir.Id`, so
                // it is always populated and made the constraint impossible to violate.
                satisfied = hasTextTitle;
                detail = "缺少图表标题（需要 role=title 的文本块）";
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

        // Carry the caller's note through. It was accepted, stored on the spec, and never read — a
        // caller who wrote "交付前必须补标题" got a verdict with their reasoning stripped, which reads as
        // though the constraint arrived without it.
        var reason = string.IsNullOrWhiteSpace(constraint.Note)
            ? detail
            : $"{detail}（备注：{constraint.Note}）";

        return new[]
        {
            new UnmetConstraint
            {
                Code = constraint.Code,
                Target = constraint.Target,
                Reason = reason,
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
    /// <summary>Counts objects whose bounds fall outside the page.</summary>
    private static int CountOutsidePage(Model.Page page)
    {
        if (page.Size is not { Width: > 0, Height: > 0 } size) return 0;

        const double Eps = 1e-6;

        static bool Outside(Model.Bounds? b, double w, double h)
        {
            if (b is null) return false;

            return b.X - b.W / 2 < -Eps || b.X + b.W / 2 > w + Eps ||
                   b.Y - b.H / 2 < -Eps || b.Y + b.H / 2 > h + Eps;
        }

        int count = 0;

        foreach (var node in page.Nodes) if (Outside(node.Bounds, size.Width, size.Height)) count++;
        foreach (var group in page.Groups) if (Outside(group.Bounds, size.Width, size.Height)) count++;
        foreach (var text in page.TextBlocks) if (Outside(text.Bounds, size.Width, size.Height)) count++;

        return count;
    }

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
