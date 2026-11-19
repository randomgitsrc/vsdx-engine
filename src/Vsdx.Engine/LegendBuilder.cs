using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Derives the rows of a legend from what a diagram actually contains.
///
/// The information already exists in structured form — `Palettes` maps role to colour, `ShapeForRole` maps role to
/// master — so a legend does not need the caller to describe it a second time, and must not become a fixed list
/// that describes roles the drawing does not use. Entries are collected from the nodes on the page.
///
/// Shape is included only when the diagram uses more than one master: a legend whose every row says "Rectangle"
/// is noise, and the point of the row is the colour-to-meaning mapping.
/// </summary>
public static class LegendBuilder
{
    /// <summary>One line of a legend: what a role looks like and what it is called.</summary>
    public sealed record Entry(string Role, string Label, string Fill, string Shape);

    /// <summary>
    /// Rows for the roles used by the diagram, in first-appearance order.
    ///
    /// First-appearance rather than palette order: the legend then reads in the same sequence the eye travels
    /// through the drawing.
    /// </summary>
    public static IReadOnlyList<Entry> Entries(Diagram diagram, string? paletteName)
    {
        if (!Palettes.TryGet(paletteName, out var palette)) Palettes.TryGet("enterprise", out palette!);

        var entries = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Container-ish roles describe structure, not a kind of box to look for. Listing them would tell the
        // reader to hunt for a shape that is in fact the band around everything else.
        var structural = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "region", "zone", "boundary", "container", "group", "layer", "lane"
        };

        var roles = diagram.Pages
            .SelectMany(p => p.Nodes)
            .Select(n => (Node: n, Role: RoleOf(n)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Role) && !structural.Contains(x.Role!))
            .ToList();

        // Only distinguish shapes when the diagram actually uses more than one.
        bool shapesVary = roles
            .Select(x => ShapeFor(x.Node, x.Role!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;

        foreach (var (node, role) in roles)
        {
            if (!seen.Add(role!)) continue;

            var fill = palette?.Roles.TryGetValue(role!, out var c) == true
                ? c
                : palette?.Default ?? "#FFFFFF";

            entries.Add(new Entry(
                role!,
                LabelFor(node, role!),
                fill,
                shapesVary ? ShapeFor(node, role!) : "Rectangle"));
        }

        return entries;
    }

    /// <summary>The role a node was drawn as, falling back to its declared type.</summary>
    private static string? RoleOf(Node node) =>
        string.IsNullOrWhiteSpace(node.Type) ? null : node.Type;

    private static string ShapeFor(Node node, string role) =>
        string.IsNullOrWhiteSpace(node.Shape) ? "Rectangle" : node.Shape!;

    /// <summary>
    /// A human label for a role. Kept deliberately small: these are the vocabulary the IR documents, and an
    /// unknown role falls through to its own name rather than being hidden.
    /// </summary>
    private static string LabelFor(Node node, string role) => role.ToLowerInvariant() switch
    {
        "client" => "客户端",
        "actor" => "用户角色",
        "gateway" => "网关",
        "service" => "服务",
        "database" => "数据库",
        "storage" => "存储",
        "queue" => "消息队列",
        "decision" => "判断",
        "monitor" => "监控",
        "external" => "外部系统",
        _ => role
    };
}
