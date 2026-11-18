using System.Text.Json;
using System.Text.Json.Serialization;
using Vsdx.Model;

namespace Vsdx.Protocol;

/// <summary>
/// Diagram IR — *what* the diagram says, with no layout decisions baked in.
///
/// Phase 7 §7.1 asks for the content/semantics layer to be separable from the design layer.
/// The existing `Diagram` model fuses both (it carries absolute `bounds`), which is fine for
/// storage but wrong for an agent boundary: a caller should be able to describe a topology
/// without deciding where every box sits.
///
/// This type is an *input* shape. It converts to `Diagram` with positions left unset, so the
/// engine — not the caller — decides geometry.
/// </summary>
public sealed class DiagramIr
{
    public string IrVersion { get; set; } = ProtocolVersion.Ir;

    public string Id { get; set; } = "diagram";
    public string? Title { get; set; }

    /// <summary>
    /// Semantic kind: architecture | microservices | workflow | network | org | deployment | hybrid.
    /// Drives strategy suggestion; not a template selector.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// Pages. When empty, the top-level Nodes/Edges/Containers form a single implicit page, so
    /// existing single-page documents keep working unchanged.
    /// </summary>
    public List<IrPage> Pages { get; set; } = new();

    /// <summary>Single-page shorthand. Ignored when <see cref="Pages"/> is non-empty.</summary>
    public List<IrNode> Nodes { get; set; } = new();
    public List<IrEdge> Edges { get; set; } = new();
    public List<IrContainer> Containers { get; set; } = new();
    public List<IrTextBlock> TextBlocks { get; set; } = new();
    public List<IrPath> Paths { get; set; } = new();
    public IrMetadata Metadata { get; set; } = new();
}

/// <summary>One page of a multi-page diagram.</summary>
public sealed class IrPage
{
    public string Id { get; set; } = "page-1";
    public string? Name { get; set; }

    public List<IrNode> Nodes { get; set; } = new();
    public List<IrEdge> Edges { get; set; } = new();
    public List<IrContainer> Containers { get; set; } = new();
    public List<IrTextBlock> TextBlocks { get; set; } = new();
    public List<IrPath> Paths { get; set; } = new();
}

/// <summary>
/// Standalone text: a title, subtitle, caption, legend or footnote.
///
/// These are not nodes. Expressing a title as a zero-edge node would put it in the layout graph
/// and corrupt every spacing and quality metric, so the protocol keeps them separate.
/// </summary>
public sealed class IrTextBlock
{
    public string Id { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>title | subtitle | caption | legend | note. Drives the default point size.</summary>
    public string? Role { get; set; }

    /// <summary>
    /// Required position and box, in inches. Unlike a node, text is not laid out: where it goes is
    /// a design decision, so the protocol asks for it rather than guessing.
    /// </summary>
    public IrBox? Box { get; set; }

    /// <summary>left | center | right.</summary>
    public string? Align { get; set; }

    /// <summary>Explicit point size. Overrides the role default.</summary>
    public double? FontSize { get; set; }

    public string? Color { get; set; }
    public string? Font { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
}

/// <summary>
/// A shape whose outline is explicit geometry rather than a built-in master.
///
/// Rows are given as path commands. <c>MoveTo</c>/<c>LineTo</c> are interpreted;
/// <c>ArcTo</c>/<c>SplineKnot</c> and anything else are carried through verbatim, so a curve
/// survives even though the engine does not compute it.
/// </summary>
public sealed class IrPath
{
    public string Id { get; set; } = "";

    public string? Name { get; set; }

    /// <summary>Position and box in inches. Geometry coordinates are local to this box.</summary>
    public IrBox? Box { get; set; }

    /// <summary>Closed by default when the rows end near where they started.</summary>
    public bool? Filled { get; set; }

    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }
    public string? Fill { get; set; }

    public List<IrPathRow> Rows { get; set; } = new();
}

public sealed class IrPathRow
{
    /// <summary>MoveTo | LineTo | ArcTo | EllipticalArcTo | SplineKnot | PolylineTo.</summary>
    public string Type { get; set; } = "LineTo";

    /// <summary>Cell values by name: X, Y (and A, B, C, D for curves).</summary>
    public Dictionary<string, string> Cells { get; set; } = new();
}

/// <summary>A rectangle in inches, expressed as position plus size.</summary>
public sealed class IrBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 2;
    public double Height { get; set; } = 1;
}

public sealed class IrNode
{
    /// <summary>Stable identifier. Must be unique within the IR and survive edits.</summary>
    public string Id { get; set; } = "";

    public string? Label { get; set; }

    /// <summary>
    /// Semantic role: service | database | gateway | queue | client | actor | decision |
    /// storage | monitor | external. Drives shape choice and colour grouping.
    /// </summary>
    public string? Role { get; set; }

    public string? ContainerId { get; set; }

    /// <summary>Free-form facts the caller wants preserved (owner, SLA, version …).</summary>
    public Dictionary<string, string>? Attributes { get; set; }

    /// <summary>
    /// Optional fixed position. Supplying it pins the node; omitting it lets the engine place
    /// the node. IR must not degenerate into a coordinate list, so this stays optional.
    /// </summary>
    public IrAnchor? Anchor { get; set; }
}

public sealed class IrAnchor
{
    public double? X { get; set; }
    public double? Y { get; set; }
    /// <summary>fixed | relative. "fixed" pins absolutely; "relative" is a hint only.</summary>
    public string Mode { get; set; } = "fixed";
}

public sealed class IrEdge
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";

    /// <summary>
    /// Relationship semantics: calls | reads | writes | publishes | consumes | depends |
    /// contains | flows. Drives line style and arrow defaults.
    /// </summary>
    public string? Relation { get; set; }

    public string? Label { get; set; }

    /// <summary>Optional port hints: left|right|top|bottom|auto.</summary>
    public string? FromPort { get; set; }
    public string? ToPort { get; set; }

    /// <summary>
    /// Where along the route the label sits, 0 = source, 1 = target. Null means the default (0.5).
    ///
    /// Added because the model, the writer and the analyser all supported it while the IR did not:
    /// an agent could not move a label away from a neighbouring one, so overlapping labels had no
    /// protocol-level remedy. Optional, so existing documents keep working unchanged.
    /// </summary>
    public double? LabelPosition { get; set; }
}

public sealed class IrContainer
{
    public string Id { get; set; } = "";
    public string? Label { get; set; }

    /// <summary>zone | layer | lane | cluster | boundary. "lane" also implies ordered stacking.</summary>
    public string? Kind { get; set; }

    public string? ParentId { get; set; }

    /// <summary>Optional ordering hint for stacked bands or lanes (lower = higher on the page).</summary>
    public int? Order { get; set; }
}

public sealed class IrMetadata
{
    public string? Description { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset? Created { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

/// <summary>Protocol version constants and compatibility rules.</summary>
public static class ProtocolVersion
{
    public const string Ir = "1.0";
    public const string DesignSpec = "1.0";
    public const string ExecutionResult = "1.0";

    /// <summary>
    /// A consumer accepts a document whose major version matches and whose minor is not newer
    /// than it understands. Unknown *fields* are ignored; unknown *major* versions are rejected
    /// rather than guessed at.
    /// </summary>
    public static bool IsCompatible(string? documentVersion, string supportedMajor)
    {
        if (string.IsNullOrWhiteSpace(documentVersion)) return false;
        var parts = documentVersion.Split('.');
        if (parts.Length == 0) return false;
        return string.Equals(parts[0], supportedMajor, StringComparison.Ordinal);
    }
}

/// <summary>Converts Diagram IR into the engine's internal model without deciding geometry.</summary>
public static class DiagramIrConverter
{
    /// <summary>
    /// Builds a <see cref="Diagram"/> whose nodes carry only identity and text. Positions are
    /// left at the origin so the layout stage owns them.
    /// </summary>
    public static Diagram ToDiagram(DiagramIr ir)
    {
        var diagram = new Diagram
        {
            Id = ir.Id,
            Name = ir.Title ?? ir.Id,
            Type = ir.Kind,
            ModelVersion = ModelVersion.Current
        };

        // Multi-page when Pages is supplied, otherwise the single-page shorthand.
        var pages = ir.Pages.Count > 0
            ? ir.Pages
            : new List<IrPage>
            {
                new()
                {
                    Id = "page-1",
                    Name = ir.Title ?? "Page-1",
                    Nodes = ir.Nodes,
                    Edges = ir.Edges,
                    Containers = ir.Containers,
                    TextBlocks = ir.TextBlocks,
                    Paths = ir.Paths
                }
            };

        for (int i = 0; i < pages.Count; i++)
        {
            var source = pages[i];
            var page = new Page
            {
                Id = string.IsNullOrWhiteSpace(source.Id) ? $"page-{i + 1}" : source.Id,
                Name = source.Name ?? ir.Title ?? $"Page-{i + 1}"
            };
            diagram.Pages.Add(page);

            // Containers first, so nodes can reference them.
            foreach (var container in source.Containers)
            {
                page.Groups.Add(new Group
                {
                    Id = container.Id,
                    Name = container.Label ?? container.Id,
                    // Lanes must survive the conversion. Both the swimlane engine and the
                    // auto-strategy test for `Kind == "lane"`, so rewriting it to "container"
                    // silently fell back to layered placement — while the result still reported
                    // `layout=Swimlane`.
                    Kind = container.Kind ?? "container",
                    ParentId = container.ParentId,
                    Order = container.Order
                });
            }

            // Every node used to get the same placeholder X, so the layout engine's ordering by
            // Bounds.X had nothing to sort on and fell through to id order — alphabetical, and
            // unrelated to how the author wrote the diagram. A small increasing offset preserves
            // the declaration sequence through that sort without pretending to be a real position.
            int declarationIndex = 0;

            foreach (var node in source.Nodes)
            {
                var model = new Node
                {
                    Id = node.Id,
                    Name = node.Label ?? node.Id,
                    Text = node.Label ?? node.Id,
                    Type = node.Role,
                    Shape = ShapeForRole(node.Role),
                    GroupId = node.ContainerId,
                    Bounds = new Bounds(declarationIndex++ * 0.01, 0, 2.0, 0.9)
                };

                if (node.Attributes is { Count: > 0 })
                    model.ShapeData = new Dictionary<string, string>(node.Attributes, StringComparer.Ordinal);

                // A fixed anchor is honoured; otherwise the engine places the node.
                if (node.Anchor is { Mode: "fixed", X: not null, Y: not null })
                {
                    model.Bounds.X = node.Anchor.X.Value;
                    model.Bounds.Y = node.Anchor.Y.Value;
                }

                page.Nodes.Add(model);

                if (node.ContainerId is not null)
                {
                    var group = page.Groups.FirstOrDefault(g => g.Id == node.ContainerId);
                    group?.Members.Add(node.Id);
                }
            }

            foreach (var edge in source.Edges)
            {
                page.Edges.Add(new Edge
                {
                    Id = edge.Id,
                    From = edge.From,
                    To = edge.To,
                    Kind = "straight",
                    Label = edge.Label ?? edge.Relation,
                    FromPort = edge.FromPort,
                    ToPort = edge.ToPort,
                    LabelPosition = edge.LabelPosition,
                    EndArrow = RelationUsesArrow(edge.Relation) ? "triangle" : "none"
                });
            }

            foreach (var text in source.TextBlocks)
            {
                page.TextBlocks.Add(new TextBlock
                {
                    Id = text.Id,
                    Text = text.Text,
                    Role = text.Role,
                    Align = text.Align,
                    Bounds = text.Box is null
                        ? new Bounds(1, 1, 4, 0.5)
                        : new Bounds(text.Box.X, text.Box.Y, text.Box.Width, text.Box.Height),
                    Style = new ShapeStyle
                    {
                        FontSize = text.FontSize,
                        TextColor = text.Color,
                        Font = text.Font,
                        Bold = text.Bold,
                        Italic = text.Italic
                    }
                });
            }

            foreach (var path in source.Paths)
            {
                var geometry = new PathGeometry { Index = 0 };

                // A path with no fill is a stroke; make that explicit so Visio does not fill it.
                if (path.Filled is false || (path.Fill is null && path.Stroke is not null))
                    geometry.SectionCells = new Dictionary<string, string> { ["NoFill"] = "1" };

                foreach (var row in path.Rows)
                {
                    geometry.Rows.Add(new PathRow
                    {
                        Type = row.Type,
                        Cells = new Dictionary<string, string>(row.Cells, StringComparer.Ordinal)
                    });
                }

                page.Paths.Add(new FreePath
                {
                    Id = path.Id,
                    Name = path.Name ?? path.Id,
                    Bounds = path.Box is null
                        ? new Bounds(1, 1, 4, 1)
                        : new Bounds(path.Box.X, path.Box.Y, path.Box.Width, path.Box.Height),
                    Style = new ShapeStyle
                    {
                        Fill = path.Fill,
                        Stroke = path.Stroke,
                        StrokeWidth = path.StrokeWidth
                    },
                    Geometry = { geometry }
                });
            }
        }

        // Inherit the container hierarchy into member lists.
        DiagramJson.Normalize(diagram);
        return diagram;
    }

    /// <summary>Maps a semantic role to a Visio shape family.</summary>
    public static string ShapeForRole(string? role) => role?.ToLowerInvariant() switch
    {
        "gateway" => "Hexagon",
        "database" or "storage" => "Process",
        "queue" or "bus" => "Parallelogram",
        "client" or "actor" or "user" => "Circle",
        "decision" => "Diamond",
        "monitor" => "Hexagon",
        "external" => "Rectangle",
        "zone" or "boundary" => "Rectangle",
        _ => "Rectangle"
    };

    /// <summary>Relations that imply an arrowhead; "contains" does not.</summary>
    public static bool RelationUsesArrow(string? relation) =>
        relation?.ToLowerInvariant() is not ("contains" or null);
}
