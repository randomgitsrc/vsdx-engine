using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace Vsdx.Model;

/// <summary>Loads and saves the JSON diagram model with stable, diff-friendly formatting.</summary>
public static class DiagramJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static Diagram Load(string path)
    {
        var json = File.ReadAllText(path);
        return Deserialize(json);
    }

    public static Diagram Deserialize(string json)
    {
        var diagram = JsonSerializer.Deserialize<Diagram>(json, Options)
                      ?? throw new InvalidOperationException("Empty diagram model.");
        Normalize(diagram);
        return diagram;
    }

    public static string Serialize(Diagram diagram)
    {
        Normalize(diagram);
        return JsonSerializer.Serialize(diagram, Options);
    }

    public static void Save(Diagram diagram, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Serialize(diagram));
    }

    /// <summary>
    /// Fills in derived fields (group membership, layer membership) so that hand-written
    /// JSON only has to declare one side of a relationship.
    /// </summary>
    public static void Normalize(Diagram diagram)
    {
        diagram.ModelVersion = string.IsNullOrWhiteSpace(diagram.ModelVersion)
            ? Vsdx.Model.ModelVersion.Current
            : diagram.ModelVersion;
        diagram.Metadata.ModelVersion = diagram.ModelVersion;

        if (diagram.PageSize is null && diagram.Pages.Count > 0 && diagram.Pages[0].Size is not null)
            diagram.PageSize = diagram.Pages[0].Size;

        if (diagram.Pages.Count == 0)
            diagram.Pages.Add(new Page { Id = "page-1", Name = "Page-1", Size = diagram.PageSize });

        for (int i = 0; i < diagram.Pages.Count; i++)
        {
            var page = diagram.Pages[i];
            // Inherit a *copy*, not the shared instance: the sizer mutates page.Size, and an
            // aliased object would silently rewrite diagram.PageSize too.
            page.Size ??= CloneSize(diagram.PageSize);
            page.Background ??= diagram.Background;
            page.NameU ??= page.Name;

            var nodeIds = new HashSet<string>(page.Nodes.Select(n => n.Id), StringComparer.Ordinal);
            var groupById = page.Groups.Where(g => !string.IsNullOrEmpty(g.Id))
                                       .GroupBy(g => g.Id, StringComparer.Ordinal)
                                       .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            // Node.GroupId is authoritative when both sides are present.
            foreach (var node in page.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.GroupId)) continue;
                if (!groupById.TryGetValue(node.GroupId!, out var group))
                {
                    node.GroupId = null;
                    continue;
                }
                if (!group.Members.Contains(node.Id, StringComparer.Ordinal))
                    group.Members.Add(node.Id);
            }

            // Groups may also declare membership only through Members.
            foreach (var group in page.Groups)
            {
                group.Members = group.Members.Where(nodeIds.Contains).Distinct(StringComparer.Ordinal).ToList();
                foreach (var memberId in group.Members)
                {
                    var node = page.Nodes.First(n => n.Id == memberId);
                    node.GroupId ??= group.Id;
                }
            }

            // Layers: derive Members from memberships declared on nodes/edges/groups.
            foreach (var layer in page.Layers)
            {
                layer.NameU ??= layer.Name;
                var declared = new List<string>(layer.Members);
                foreach (var node in page.Nodes.Where(n => n.Layers is not null && n.Layers.Contains(layer.Name)))
                    if (!declared.Contains(node.Id, StringComparer.Ordinal)) declared.Add(node.Id);
                foreach (var edge in page.Edges.Where(e => e.Layers is not null && e.Layers.Contains(layer.Name)))
                    if (!declared.Contains(edge.Id, StringComparer.Ordinal)) declared.Add(edge.Id);
                foreach (var group in page.Groups.Where(g => g.Layers is not null && g.Layers.Contains(layer.Name)))
                    if (!declared.Contains(group.Id, StringComparer.Ordinal)) declared.Add(group.Id);
                layer.Members = declared.Distinct(StringComparer.Ordinal).ToList();
            }

            foreach (var edge in page.Edges)
            {
                if (edge.Kind is null) edge.Kind = "straight";
                if (edge.FromPort is not null) edge.FromPort = NormalizePort(edge.FromPort);
                if (edge.ToPort is not null) edge.ToPort = NormalizePort(edge.ToPort);
            }

            foreach (var node in page.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Shape)) node.Shape = "Rectangle";
                node.Name ??= node.Id;
            }
        }
    }

    private static PageSize? CloneSize(PageSize? source) =>
        source is null ? null : new PageSize
        {
            Width = source.Width,
            Height = source.Height,
            Unit = source.Unit
        };

    public static string NormalizePort(string port) => port.Trim().ToLowerInvariant() switch
    {
        "left" or "west" or "l" => "left",
        "right" or "east" or "r" => "right",
        "top" or "north" or "up" or "t" => "top",
        "bottom" or "south" or "down" or "b" => "bottom",
        _ => "auto"
    };
}
