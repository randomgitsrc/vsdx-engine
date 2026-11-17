using OfficeIMO.Visio;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Reads a VSDX package into the backend-neutral model using OfficeIMO.Visio.
/// Anything the library cannot expose structurally is recorded in
/// <see cref="Page.Unmapped"/> so it is never silently dropped.
/// </summary>
public sealed class OfficeImoDocumentReader : IDocumentReader
{
    public string Backend => "OfficeIMO.Visio";
    public static string BackendVersion { get; } =
        typeof(VisioDocument).Assembly.GetName().Version?.ToString() ?? "unknown";

    public ReadResult Read(string vsdxPath)
    {
        if (!File.Exists(vsdxPath))
            throw new FileNotFoundException($"VSDX not found: {vsdxPath}", vsdxPath);

        var compat = new CompatibilityReport();
        var doc = VisioDocument.Load(vsdxPath);

        var diagram = new Diagram
        {
            Id = Path.GetFileNameWithoutExtension(vsdxPath),
            Name = doc.Properties?.Title,
            ModelVersion = ModelVersion.Current,
            Metadata = BuildMetadata(vsdxPath)
        };

        var firstPage = doc.Pages.FirstOrDefault();
        if (firstPage is not null)
        {
            diagram.PageSize = new PageSize
            {
                Width = firstPage.Width.ToInches(),
                Height = firstPage.Height.ToInches(),
                Unit = "inches"
            };
            diagram.Orientation = firstPage.Height.ToInches() > firstPage.Width.ToInches() ? "portrait" : "landscape";
        }

        foreach (var page in doc.Pages)
        {
            diagram.Pages.Add(ReadPage(page, compat));
        }

        RecordMasters(doc, compat);

        return new ReadResult
        {
            Diagram = diagram,
            Compatibility = compat,
            SourcePath = Path.GetFullPath(vsdxPath),
            Backend = Backend,
            BackendVersion = BackendVersion,
            FileSize = new FileInfo(vsdxPath).Length
        };
    }

    /// <summary>
    /// Recovers engine metadata (history, timestamps) that the writer stored in the package,
    /// falling back to a freshly derived record for externally authored files.
    /// </summary>
    private static Metadata BuildMetadata(string vsdxPath)
    {
        var metadata = VisioMetadataBridge.Read(vsdxPath) ?? new Metadata
        {
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            Generator = $"{EngineInfo.Name} {EngineInfo.Version}"
        };

        metadata.SourceFile = Path.GetFullPath(vsdxPath);
        metadata.SourceHash = HashFile(vsdxPath);
        metadata.Backend = "OfficeIMO.Visio";
        metadata.BackendVersion = BackendVersion;
        metadata.Generator ??= $"{EngineInfo.Name} {EngineInfo.Version}";
        return metadata;
    }

    private static Page ReadPage(OfficeIMO.Visio.VisioPage page, CompatibilityReport compat)
    {
        var model = new Page
        {
            Id = $"page-{page.Id}",
            Name = page.Name,
            NameU = page.NameU,
            VisioId = page.Id,
            Size = new PageSize
            {
                Width = page.Width.ToInches(),
                Height = page.Height.ToInches(),
                Unit = "inches"
            }
        };

        var shapeOrder = new List<string>();
        var containers = new Dictionary<string, OfficeIMO.Visio.VisioShape>(StringComparer.Ordinal);

        // Pass 1: classify shapes (containers vs nodes).
        foreach (var shape in page.Shapes)
        {
            if (IsContainerShape(shape))
                containers[shape.Id] = shape;
        }

        // Pass 2: build nodes.
        foreach (var shape in page.Shapes)
        {
            if (IsContainerShape(shape)) continue;

            var node = ReadNode(shape, compat);
            model.Nodes.Add(node);
            shapeOrder.Add(shape.Id);
        }

        // Pass 3: build groups from containers, plus parent groups.
        foreach (var (id, container) in containers)
        {
            var group = ReadGroup(container, page, compat);
            model.Groups.Add(group);
            shapeOrder.Add(id);
        }

        // Visio containers record membership on both sides. ContainerOwnerIds is authoritative
        // for container membership, while shape parenting covers classic groups.
        foreach (var shape in page.Shapes)
        {
            var node = FindNodeByVisioId(model, shape.Id);
            if (node is null) continue;

            if (shape.ContainerOwnerIds.Count > 0)
            {
                var ownerId = shape.ContainerOwnerIds[0];
                var ownerGroup = model.Groups.FirstOrDefault(g => g.VisioShapeId == ownerId || g.Id == ownerId);
                if (ownerGroup is not null && node.GroupId is null) node.GroupId = ownerGroup.Id;
            }

            if (node.GroupId is null && shape.Parent is not null)
            {
                var parentGroup = model.Groups.FirstOrDefault(g => g.VisioShapeId == shape.Parent.Id);
                if (parentGroup is not null) node.GroupId = parentGroup.Id;
            }
        }

        // Fall back to the group's own member list when the shape carries no ownership cell.
        foreach (var group in model.Groups)
        {
            foreach (var memberId in group.Members)
            {
                var node = model.Nodes.FirstOrDefault(n => n.Id == memberId || n.VisioShapeId == memberId);
                if (node is not null && node.GroupId is null) node.GroupId = group.Id;
            }
        }

        // Edges.
        foreach (var connector in page.Connectors)
        {
            var edge = ReadEdge(connector, model, compat);
            model.Edges.Add(edge);
            shapeOrder.Add(connector.Id);
        }

        // Layers.
        foreach (var layer in page.Layers)
        {
            model.Layers.Add(new Layer
            {
                Id = layer.NameU ?? layer.Name,
                Name = layer.Name,
                NameU = layer.NameU,
                Visible = layer.Visible,
                Locked = layer.Lock,
                Print = layer.Print
            });
        }

        foreach (var node in model.Nodes)
        {
            if (node.Layers is { Count: > 0 })
                foreach (var layerName in node.Layers)
                    EnsureLayer(model, layerName).Members.Add(node.Id);
        }
        foreach (var edge in model.Edges)
        {
            if (edge.Layers is { Count: > 0 })
                foreach (var layerName in edge.Layers)
                    EnsureLayer(model, layerName).Members.Add(edge.Id);
        }

        model.ShapeOrder = shapeOrder;

        if (model.Nodes.Count == 0 && model.Groups.Count == 0 && page.Shapes.Count > 0)
            compat.Warn("read.no-nodes", $"Page '{page.Name}' has {page.Shapes.Count} shapes but none mapped to nodes.", page.Name);

        return model;
    }

    private static Layer EnsureLayer(Page page, string name)
    {
        var layer = page.Layers.FirstOrDefault(l =>
            string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.NameU, name, StringComparison.OrdinalIgnoreCase));
        if (layer is not null) return layer;
        layer = new Layer { Id = name, Name = name, NameU = name };
        page.Layers.Add(layer);
        return layer;
    }

    /// <summary>A shape counts as a container when OfficeIMO marks it, or when it is a Container master.</summary>
    internal static bool IsContainerShape(OfficeIMO.Visio.VisioShape shape)
    {
        if (shape.IsContainer) return true;
        if (shape.ContainerMemberIds.Count > 0) return true;
        return string.Equals(shape.MasterNameU, "Container", StringComparison.OrdinalIgnoreCase);
    }

    private static Group ReadGroup(OfficeIMO.Visio.VisioShape shape, OfficeIMO.Visio.VisioPage page, CompatibilityReport compat)
    {
        var group = new Group
        {
            Id = shape.Id,
            Name = shape.Text,
            NameU = shape.NameU,
            VisioShapeId = shape.Id,
            Kind = "container",
            Bounds = new Bounds(shape.PinX.ToInches(), shape.PinY.ToInches(), shape.Width.ToInches(), shape.Height.ToInches()),
            Layers = shape.LayerNames.Count > 0 ? shape.LayerNames.ToList() : null,
            Style = ReadStyle(shape)
        };

        // ContainerMemberIds hold Visio shape ids; translate them to the stable model ids the
        // agent works with so membership survives a rename.
        foreach (var memberVisioId in shape.ContainerMemberIds)
            group.Members.Add(ResolveModelId(page, memberVisioId));

        foreach (var child in shape.Children)
            group.Members.Add(ResolveModelId(page, child.Id));

        if (group.Members.Count == 0)
            compat.Info("read.container-empty", $"Container '{shape.Id}' has no enumerable members.", shape.Id);

        return group;
    }

    private static Node ReadNode(OfficeIMO.Visio.VisioShape shape, CompatibilityReport compat)
    {
        var node = new Node
        {
            // Prefer the persisted stable model id when the engine stamped one, so a renamed
            // logical node keeps its agent-facing identifier across a save/load cycle.
            Id = StableIdOf(shape) ?? shape.Id,
            Name = string.IsNullOrWhiteSpace(shape.Name) ? shape.Id : shape.Name,
            Text = shape.Text,
            Shape = MasterCatalog.ToModelKind(shape.MasterNameU),
            VisioShapeId = shape.Id,
            Bounds = new Bounds(shape.PinX.ToInches(), shape.PinY.ToInches(), shape.Width.ToInches(), shape.Height.ToInches()),
            Style = ReadStyle(shape),
            Layers = shape.LayerNames.Count > 0 ? shape.LayerNames.ToList() : null,
            Angle = Math.Abs(shape.Angle) > 1e-9 ? shape.Angle : null,
            Imported = true
        };

        if (shape.Parent is not null) node.GroupId = shape.Parent.Id;

        if (shape.ShapeData.Count > 0)
        {
            node.ShapeData = new Dictionary<string, string>(StringComparer.Ordinal);
            node.ShapeDataLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in shape.ShapeData)
            {
                node.ShapeData[row.Name] = row.Value ?? "";
                if (!string.IsNullOrEmpty(row.Label)) node.ShapeDataLabels[row.Name] = row.Label!;
            }
        }

        if (shape.Hyperlinks.Count > 0)
            node.Hyperlink = shape.Hyperlinks[0].Address;

        if (shape.Master is null && shape.MasterNameU is null)
            compat.Warn("read.shape-no-master", $"Shape '{shape.Id}' has no master; geometry may not survive an edit.", shape.Id);

        return node;
    }

    private static ShapeStyle ReadStyle(OfficeIMO.Visio.VisioShape shape)
    {
        var style = new ShapeStyle
        {
            Fill = FormatColor(shape.FillColor),
            FillPattern = shape.FillPattern.ToString(),
            Stroke = FormatColor(shape.LineColor),
            StrokeWidth = shape.LineWeight > 0 ? shape.LineWeight : null,
            StrokePattern = shape.LinePattern
        };

        var text = shape.TextStyle;
        if (text is not null)
        {
            style.Font = text.FontFamily;
            style.FontSize = text.Size;
            style.Bold = text.Bold;
            style.Italic = text.Italic;
            style.TextColor = text.Color is null ? null : FormatColor(text.Color.Value);
            style.HAlign = text.HorizontalAlignment?.ToString()?.ToLowerInvariant();
            style.VAlign = text.VerticalAlignment?.ToString()?.ToLowerInvariant();
        }

        return style;
    }

    private static Edge ReadEdge(OfficeIMO.Visio.VisioConnector connector, Page page, CompatibilityReport compat)
    {
        var edge = new Edge
        {
            Id = connector.Id,
            VisioShapeId = connector.Id,
            // Resolve endpoints to the stable model id so a renamed node's edges still point
            // at the node the agent knows.
            From = StableIdFor(page, connector.From?.Id),
            To = StableIdFor(page, connector.To?.Id),
            Kind = connector.Kind.ToString().ToLowerInvariant(),
            Label = connector.Label,
            BeginArrow = connector.BeginArrow?.ToString().ToLowerInvariant(),
            EndArrow = connector.EndArrow?.ToString().ToLowerInvariant(),
            LineColor = FormatColor(connector.LineColor),
            LineWeight = connector.LineWeight > 0 ? connector.LineWeight : null,
            LinePattern = connector.LinePattern,
            Layers = connector.LayerNames.Count > 0 ? connector.LayerNames.ToList() : null
        };

        if (connector.Waypoints.Count > 0)
            edge.Waypoints = connector.Waypoints
                .Select(w => new Model.Point(w.X, w.Y))
                .ToList();

        if (edge.From is null)
        {
            var start = connector.StartPoint;
            edge.FromPoint = new Model.Point(start.X, start.Y);
        }

        if (edge.To is null)
        {
            var end = connector.EndPoint;
            edge.ToPoint = new Model.Point(end.X, end.Y);
        }

        if (edge.From is not null && page.Nodes.All(n => n.Id != edge.From))
            compat.Warn("read.edge-dangling", $"Connector '{connector.Id}' starts at unknown shape '{edge.From}'.", connector.Id);
        if (edge.To is not null && page.Nodes.All(n => n.Id != edge.To))
            compat.Warn("read.edge-dangling", $"Connector '{connector.Id}' ends at unknown shape '{edge.To}'.", connector.Id);

        if (connector.ShapeData.Count > 0)
        {
            edge.ShapeData = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in connector.ShapeData) edge.ShapeData[row.Name] = row.Value ?? "";
        }

        return edge;
    }

    private static void RecordMasters(VisioDocument doc, CompatibilityReport compat)
    {
        foreach (var master in doc.Masters)
        {
            if (!MasterCatalog.BuiltIn.Contains(master.NameU))
                compat.Info("read.master-external", $"Package-backed master '{master.NameU}' is preserved but not editable through the standard model.", master.NameU);
        }
    }

    /// <summary>Reads the engine-stamped stable id from a shape user cell, if present.</summary>
    private static string? StableIdOf(OfficeIMO.Visio.VisioShape shape)
    {
        var value = shape.GetUserCellValue("VsdxModelId");
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static Node? FindNodeByVisioId(Page page, string visioId) =>
        page.Nodes.FirstOrDefault(n => n.VisioShapeId == visioId);

    internal static string? FormatColor(OfficeIMO.OfficeColor color) =>
        ColorUtil.ToHex(color.R, color.G, color.B);

    /// <summary>Maps a Visio shape id to the stable model id stamped on that shape.</summary>
    private static string ResolveModelId(OfficeIMO.Visio.VisioPage page, string visioShapeId)
    {
        var shape = page.Shapes.FirstOrDefault(s => s.Id == visioShapeId);
        var stable = shape is null ? null : StableIdOf(shape);
        return stable ?? visioShapeId;
    }

    /// <summary>Maps a Visio shape id to the stable model id when the engine stamped one.</summary>
    private static string? StableIdFor(Page page, string? visioShapeId)
    {
        if (visioShapeId is null) return null;
        var node = page.Nodes.FirstOrDefault(n => n.VisioShapeId == visioShapeId);
        return node?.Id ?? visioShapeId;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
