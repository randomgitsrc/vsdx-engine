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
                    Containers = ir.Containers
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
                    Kind = container.Kind is "lane" ? "container" : (container.Kind ?? "container"),
                    ParentId = container.ParentId
                });
            }

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
                    Bounds = new Bounds(0, 0, 2.0, 0.9)
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
                    EndArrow = RelationUsesArrow(edge.Relation) ? "triangle" : "none"
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
