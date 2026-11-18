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
            diagram.Pages.Add(ReadPage(page, vsdxPath, compat));
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

    /// <summary>
    /// Reads raw <c>Master</c> attributes from the page part, keyed by shape id.
    /// OfficeIMO projects a resolved master name but not the original reference, and the
    /// reference is what has to survive a round-trip.
    /// </summary>
    private static Dictionary<string, string> ReadSourceMasters(
        string vsdxPath, int pageId, CompatibilityReport compat)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(vsdxPath);
            var entry = zip.GetEntry($"visio/pages/page{pageId + 1}.xml");
            if (entry is null) return result;

            System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
            System.Xml.Linq.XDocument doc;
            using (var stream = entry.Open()) doc = System.Xml.Linq.XDocument.Load(stream);

            foreach (var shape in doc.Descendants(ns + "Shape"))
            {
                var id = (string?)shape.Attribute("ID");
                var master = (string?)shape.Attribute("Master");
                if (id is not null && master is not null) result[id] = master;
            }
        }
        catch (Exception ex)
        {
            compat.Warn("read.master-ref", $"无法解析母版引用：{ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Parses every Geometry section in a page part, keyed by Visio shape id.
    ///
    /// OfficeIMO does not project geometry onto its shape model, so this reads the XML directly.
    /// Rows of types the engine does not interpret are kept verbatim rather than dropped: the
    /// reviewer's requirement is that an unsupported geometry instruction is preserved, not
    /// converted into something else.
    /// </summary>
    private static Dictionary<string, List<PathGeometry>> ReadGeometrySections(
        string vsdxPath, int pageId, CompatibilityReport compat)
    {
        var result = new Dictionary<string, List<PathGeometry>>(StringComparer.Ordinal);

        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(vsdxPath);
            var entry = zip.GetEntry($"visio/pages/page{pageId + 1}.xml");
            if (entry is null) return result;

            System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
            System.Xml.Linq.XDocument doc;
            using (var stream = entry.Open()) doc = System.Xml.Linq.XDocument.Load(stream);

            foreach (var shape in doc.Descendants(ns + "Shape"))
            {
                var shapeId = (string?)shape.Attribute("ID");
                if (shapeId is null) continue;

                var sections = shape.Elements(ns + "Section")
                    .Where(sec => (string?)sec.Attribute("N") == "Geometry")
                    .ToList();

                if (sections.Count == 0) continue;

                var geometries = new List<PathGeometry>();
                foreach (var section in sections)
                {
                    var geometry = new PathGeometry
                    {
                        Index = int.TryParse((string?)section.Attribute("IX"), out int ix) ? ix : 0
                    };

                    var sectionCells = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var cell in section.Elements(ns + "Cell"))
                    {
                        var name = (string?)cell.Attribute("N");
                        var value = (string?)cell.Attribute("V");
                        if (name is not null && value is not null) sectionCells[name] = value;
                    }
                    if (sectionCells.Count > 0) geometry.SectionCells = sectionCells;

                    foreach (var row in section.Elements(ns + "Row"))
                    {
                        var type = (string?)row.Attribute("T");
                        if (type is null) continue;

                        var pathRow = new PathRow { Type = type };
                        foreach (var cell in row.Elements(ns + "Cell"))
                        {
                            var name = (string?)cell.Attribute("N");
                            var value = (string?)cell.Attribute("V");
                            if (name is not null && value is not null) pathRow.Cells[name] = value;
                        }
                        geometry.Rows.Add(pathRow);
                    }

                    geometries.Add(geometry);
                }

                result[shapeId] = geometries;
            }
        }
        catch (Exception ex)
        {
            compat.Warn("read.geometry", $"无法解析自定义几何：{ex.Message}");
        }

        return result;
    }

    /// <summary>Builds a TextBlock from a text-box shape.</summary>
    private static TextBlock ReadTextBlock(
        OfficeIMO.Visio.VisioShape shape, CompatibilityReport compat)
    {
        var block = new TextBlock
        {
            Id = StableIdOf(shape) ?? shape.Id,
            Name = shape.Text,
            Text = shape.Text ?? "",
            VisioShapeId = shape.Id,
            Bounds = new Bounds(
                shape.PinX.ToInches(), shape.PinY.ToInches(),
                shape.Width.ToInches(), shape.Height.ToInches()),
            Style = ReadStyle(shape),
            Imported = true
        };

        // Recover the semantic role from the stamped name when present, so a re-read block keeps
        // its typography intent instead of being treated as a generic caption.
        // The writer stores the model id in Name, so the id carries the role hint. Recognise a
        // role by id prefix first, then fall back to a generic caption.
        var hint = (shape.Name ?? "").ToLowerInvariant();
        block.Role = hint switch
        {
            _ when hint.StartsWith("title", StringComparison.Ordinal) => "title",
            _ when hint.StartsWith("subtitle", StringComparison.Ordinal) => "subtitle",
            _ when hint.StartsWith("legend", StringComparison.Ordinal) => "legend",
            _ when hint.StartsWith("note", StringComparison.Ordinal) => "note",
            _ => "caption"
        };

        return block;
    }

    /// <summary>Builds a FreePath from a shape and its parsed geometry.</summary>
    private static FreePath ReadFreePath(
        OfficeIMO.Visio.VisioShape shape, List<PathGeometry> geometry, CompatibilityReport compat)
    {
        var path = new FreePath
        {
            // Prefer the engine-stamped stable id so a renamed path keeps its identity, the same
            // way nodes do.
            Id = StableIdOf(shape) ?? shape.Id,
            Name = shape.Text,
            NameU = shape.NameU,
            VisioShapeId = shape.Id,
            Text = shape.Text,
            Bounds = new Bounds(
                shape.PinX.ToInches(), shape.PinY.ToInches(),
                shape.Width.ToInches(), shape.Height.ToInches()),
            Geometry = geometry,
            Style = ReadStyle(shape)
        };

        if (shape.LayerNames.Count > 0) path.Layers = shape.LayerNames.ToList();

        // Flag geometry containing instructions the engine does not interpret, so a caller knows
        // this path is preserved rather than understood.
        var unknown = geometry
            .SelectMany(g => g.Rows)
            .Select(r => r.Type)
            .Where(t => t is not ("MoveTo" or "LineTo"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unknown.Count > 0)
        {
            compat.Info("read.geometry-verbatim",
                $"形状 '{shape.Id}' 含未解释的几何指令（{string.Join(", ", unknown)}），已原样保留",
                shape.Id);
        }

        return path;
    }

    /// <summary>
    /// Reads shape→parent links from the page part and resolves them to container headings.
    ///
    /// OfficeIMO.Visio does not project the <c>Parent</c> cell onto <c>VisioShape.Parent</c> on
    /// load, so nested containers were flattened on every read. The cell is the only record
    /// Visio keeps of the hierarchy, so it must be parsed from the XML. Package shape ids are
    /// sequential, so the container heading (which the model stores as Group.Name) is used to
    /// join the two representations.
    /// </summary>
    private static List<(string childHeading, string parentHeading)> ResolveParentLinksByHeading(
        string vsdxPath, int pageId, IReadOnlyList<Group> groups, CompatibilityReport compat)
    {
        var result = new List<(string, string)>();
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(vsdxPath);
            var entry = zip.GetEntry($"visio/pages/page{pageId + 1}.xml");
            if (entry is null) return result;

            System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
            System.Xml.Linq.XDocument doc;
            using (var stream = entry.Open()) doc = System.Xml.Linq.XDocument.Load(stream);

            var headingById = new Dictionary<string, string>(StringComparer.Ordinal);
            var parentOfId = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var shape in doc.Descendants(ns + "Shape"))
            {
                var id = (string?)shape.Attribute("ID");
                if (id is null) continue;

                var text = ((string?)shape.Element(ns + "Text"))?.Trim() ?? "";
                if (!string.IsNullOrEmpty(text)) headingById[id] = text;

                var parentCell = shape.Elements(ns + "Cell")
                    .FirstOrDefault(c => (string?)c.Attribute("N") == "Parent");
                var parentValue = (string?)parentCell?.Attribute("V");
                if (!string.IsNullOrWhiteSpace(parentValue)) parentOfId[id] = parentValue!;
            }

            foreach (var (childId, parentId) in parentOfId)
            {
                if (!headingById.TryGetValue(childId, out var childHeading)) continue;
                if (!headingById.TryGetValue(parentId, out var parentHeading)) continue;

                // Only report pairs that correspond to model containers.
                if (!groups.Any(g => string.Equals(g.Name, childHeading, StringComparison.Ordinal))) continue;
                if (!groups.Any(g => string.Equals(g.Name, parentHeading, StringComparison.Ordinal))) continue;

                result.Add((childHeading, parentHeading));
            }
        }
        catch (Exception ex)
        {
            compat.Warn("read.parent-links", $"无法解析嵌套容器关系：{ex.Message}");
        }
        return result;
    }

    private static Page ReadPage(OfficeIMO.Visio.VisioPage page, string vsdxPath, CompatibilityReport compat)
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

        // OfficeIMO does not expose the raw Master attribute, so capture it from the part. Without
        // it a round-trip silently drops the link to an unrecognised master: the outline is
        // materialised, so the shape still looks right, but it stops being a master instance.
        var sourceMasters = ReadSourceMasters(vsdxPath, page.Id, compat);

        var shapeOrder = new List<string>();
        var containers = new Dictionary<string, OfficeIMO.Visio.VisioShape>(StringComparer.Ordinal);

        // Pass 1: classify shapes (containers vs nodes).
        foreach (var shape in page.Shapes)
        {
            if (IsContainerShape(shape))
                containers[shape.Id] = shape;
        }

        // Pass 2: build nodes and free-form paths.
        //
        // A shape with its own Geometry section is not one of the built-in masters; treating it
        // as a node silently relabels a custom outline as a rectangle. Such shapes become
        // FreePath entries instead, keeping the raw rows so nothing is lost.
        var geometryByShape = ReadGeometrySections(vsdxPath, page.Id, compat);

        foreach (var shape in page.Shapes)
        {
            if (IsContainerShape(shape)) continue;

            // Standalone text is not a node: it has no edges and takes no part in layout.
            if (string.Equals(shape.NameU, "TextBlock", StringComparison.Ordinal))
            {
                model.TextBlocks.Add(ReadTextBlock(shape, compat));
                shapeOrder.Add(shape.Id);
                continue;
            }

            if (geometryByShape.TryGetValue(shape.Id, out var geometry) && geometry.Count > 0)
            {
                var path = ReadFreePath(shape, geometry, compat);
                model.Paths.Add(path);
                shapeOrder.Add(shape.Id);
                continue;
            }

            var node = ReadNode(shape, compat);
            if (sourceMasters.TryGetValue(shape.Id, out var masterRef)) node.SourceMaster = masterRef;
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

        // Restore container-inside-container nesting. Visio records this as a Parent cell on
        // the child shape; without reading it, hierarchy is lost on every round-trip.
        // parentLinks is keyed by package shape id; model groups carry the container heading as
        // their Name. The heading is the only stable join key between the two, so resolve the
        // package ids to headings first.
        var parentLinks = ResolveParentLinksByHeading(vsdxPath, page.Id, model.Groups, compat);

        foreach (var (childHeading, parentHeading) in parentLinks)
        {
            var childGroup = model.Groups.FirstOrDefault(g =>
                string.Equals(g.Name, childHeading, StringComparison.Ordinal));
            var parentGroup = model.Groups.FirstOrDefault(g =>
                string.Equals(g.Name, parentHeading, StringComparison.Ordinal));
            if (childGroup is null || parentGroup is null) continue;
            if (ReferenceEquals(childGroup, parentGroup)) continue;

            childGroup.ParentId = parentGroup.Id;

            // A nested container is also a member of its parent zone.
            if (!parentGroup.Members.Contains(childGroup.Id, StringComparer.Ordinal))
                parentGroup.Members.Add(childGroup.Id);

            // The child's own members belong to the parent zone transitively.
            foreach (var memberId in childGroup.Members)
                if (!parentGroup.Members.Contains(memberId, StringComparer.Ordinal))
                    parentGroup.Members.Add(memberId);
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

        // Warn only when shapes were genuinely left unmapped. A page of custom geometry or
        // standalone text has no nodes by design, so reporting "none mapped to nodes" there is a
        // false alarm that would teach a caller to ignore the warning.
        int modelled = model.Nodes.Count + model.Paths.Count + model.TextBlocks.Count;
        if (modelled == 0 && model.Groups.Count == 0 && page.Shapes.Count > 0)
        {
            compat.Warn("read.no-nodes",
                $"Page '{page.Name}' has {page.Shapes.Count} shapes but none mapped to a model object.",
                page.Name);
        }

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
            // Restored from the cell the writer sets. Absent or blank means the node never had a
            // role, which is a normal state rather than an error.
            Type = UserCellOf(shape, "VsdxRole"),
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
    private static string? StableIdOf(OfficeIMO.Visio.VisioShape shape) => UserCellOf(shape, "VsdxModelId");

    /// <summary>
    /// Reads a named user cell, treating blank as absent. User cells are the package's extension
    /// point for data Visio itself has no cell for; the engine uses them for the stable id and,
    /// now, the node's semantic role.
    /// </summary>
    private static string? UserCellOf(OfficeIMO.Visio.VisioShape shape, string name)
    {
        var value = shape.GetUserCellValue(name);
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
