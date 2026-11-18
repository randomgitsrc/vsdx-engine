using OfficeIMO;
using OfficeIMO.Visio;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>Shared engine identity used in diagnostics.</summary>
public static class EngineInfo
{
    public const string Version = "1.0.0";
    public const string Name = "Agent VSDX Engine";
}

/// <summary>
/// Writes the JSON model to a VSDX package using OfficeIMO.Visio.
/// Deterministic: the same model always yields the same Visio shape IDs and z-order.
/// </summary>
public sealed class OfficeImoDocumentWriter : IDocumentWriter
{
    public const string BackendName = "OfficeIMO.Visio";

    public WriteResult Write(Diagram diagram, string vsdxPath, WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        options ??= new WriteOptions();

        var compat = new CompatibilityReport();
        var dir = Path.GetDirectoryName(Path.GetFullPath(vsdxPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        DiagramJson.Normalize(diagram);

        ReportUnsupportedStyleFields(diagram, compat);

        var doc = VisioDocument.Create(vsdxPath);
        doc.UseMastersByDefault = true;
        doc.WriteMasterDeltasOnly = false;
        doc.ConfigureProperties(p =>
        {
            p.Title = options.Title ?? diagram.Name ?? diagram.Id;
            p.Author = options.Author ?? $"{EngineInfo.Name} {EngineInfo.Version}";
        });

        int shapeCount = 0, connectorCount = 0;

        foreach (var pageModel in diagram.Pages)
        {
            // The model's numbers are in the model's unit, and the package stores inches. Passing
            // the value through unchanged declared inches over a centimetre figure: a 10x8 cm page
            // was written as 10x8 inches, 2.5x too large. UnitFactor already knew the conversion;
            // this path never called it, while the incremental editor did — so create and update
            // disagreed about the same model.
            var pageSize = pageModel.Size ?? diagram.PageSize;
            double unitFactor = MasterCatalog.UnitFactor(pageSize?.Unit);

            var page = doc.AddPage(
                pageModel.Name ?? pageModel.Id,
                MasterCatalog.Inches((pageSize?.Width ?? 11.0) * unitFactor),
                MasterCatalog.Inches((pageSize?.Height ?? 8.5) * unitFactor),
                VisioMeasurementUnit.Inches,
                pageModel.VisioId,
                null);

            if (!string.IsNullOrWhiteSpace(pageModel.NameU)) page.NameU = pageModel.NameU!;

            page.GridVisible = false;
            page.Snap = diagram.Style?.SnapToGrid ?? true;
            page.ConnectorRouteStyle = VisioPageRouteStyle.RightAngle;

            // Layers must exist before shapes join them.
            foreach (var layer in pageModel.Layers)
            {
                var created = page.AddLayer(layer.Name, layer.NameU);
                created.Visible = layer.Visible;
                created.Lock = layer.Locked;
                created.Print = layer.Print;
            }

            // Containers first so members can reference them.
            var containerShapes = new Dictionary<string, VisioShape>(StringComparer.Ordinal);
            foreach (var group in pageModel.Groups.Where(g => g.ParentId is null))
            {
                var shape = WriteGroup(page, group, pageModel, compat);
                if (shape is not null) containerShapes[group.Id] = shape;
            }
            foreach (var group in pageModel.Groups.Where(g => g.ParentId is not null))
            {
                if (!containerShapes.TryGetValue(group.ParentId!, out var parent))
                {
                    compat.Warn("write.group-parent-missing", $"Group '{group.Id}' references missing parent '{group.ParentId}'.", group.Id);
                    var shape = WriteGroup(page, group, pageModel, compat);
                    if (shape is not null) containerShapes[group.Id] = shape;
                    continue;
                }
                // Written as a sibling first; VisioNestingBridge adds the Parent cell after the
                // package is saved, because OfficeIMO exposes no nesting API.
                var created = WriteGroup(page, group, pageModel, compat);
                if (created is not null) containerShapes[group.Id] = created;
            }

            // Nodes.
            var nodeShapes = new Dictionary<string, VisioShape>(StringComparer.Ordinal);
            var usedIds = new HashSet<string>(containerShapes.Keys, StringComparer.Ordinal);
            foreach (var node in pageModel.Nodes)
            {
                var shapeId = UniqueId(node.Id, usedIds);
                var shape = WriteNode(page, node, shapeId, diagram, compat);
                nodeShapes[node.Id] = shape;
                usedIds.Add(shapeId);
                shapeCount++;
            }

            // Attach nodes to their containers.
            foreach (var group in pageModel.Groups)
            {
                if (!containerShapes.TryGetValue(group.Id, out var container)) continue;
                var memberIds = group.Members
                    .Where(m => nodeShapes.ContainsKey(m))
                    .Select(m => nodeShapes[m].Id)
                    .ToList();
                if (memberIds.Count == 0) continue;
                try
                {
                    page.AddToContainer(container.Id, memberIds, resizeToFit: group.AutoSize ?? true);
                }
                catch (Exception ex)
                {
                    compat.Warn("write.container-attach", $"Could not attach members to container '{group.Id}': {ex.Message}", group.Id);
                }
            }

            // Edges.
            var edgeIds = new HashSet<string>(usedIds, StringComparer.Ordinal);
            foreach (var edge in pageModel.Edges)
            {
                var connector = WriteEdge(page, edge, nodeShapes, edgeIds, compat);
                if (connector is not null) connectorCount++;
            }

            // Layer membership (after shapes exist).
            foreach (var layer in pageModel.Layers)
            {
                foreach (var memberId in layer.Members)
                {
                    if (nodeShapes.TryGetValue(memberId, out var shape)) page.AddToLayer(layer.Name, shape);
                    else
                    {
                        var conn = page.Connectors.FirstOrDefault(c => c.Id == memberId);
                        if (conn is not null) page.AddToLayer(layer.Name, conn);
                    }
                }
                foreach (var node in pageModel.Nodes.Where(n => n.Layers is not null && n.Layers.Contains(layer.Name)))
                    if (nodeShapes.TryGetValue(node.Id, out var s)) page.AddToLayer(layer.Name, s);
                foreach (var edge in pageModel.Edges.Where(e => e.Layers is not null && e.Layers.Contains(layer.Name)))
                {
                    var conn = page.Connectors.FirstOrDefault(c => c.Id == edge.Id);
                    if (conn is not null) page.AddToLayer(layer.Name, conn);
                }
            }
        }

        if (options.RequestRecalc) doc.RequestRecalcOnOpen();

        doc.Save(vsdxPath);

        // Nested containers need a raw XML patch: OfficeIMO.Visio cannot express shape parenting.
        VisioNestingBridge.Apply(diagram, vsdxPath, compat);

        // Custom geometry has no OfficeIMO representation, so paths are written straight into the
        // page part. Rows are emitted verbatim, including types the engine does not interpret.
        VisioFreePathBridge.Apply(diagram, vsdxPath, compat);

        // Standalone text is not a connector or container, so it is written as native shapes.
        VisioTextBlockBridge.Apply(diagram, vsdxPath, compat);

        VisioMetadataBridge.Write(diagram, vsdxPath);

        return new WriteResult
        {
            OutputPath = Path.GetFullPath(vsdxPath),
            Compatibility = compat,
            PagesWritten = diagram.Pages.Count,
            ShapesWritten = shapeCount,
            ConnectorsWritten = connectorCount,
            FileSize = new FileInfo(vsdxPath).Length,
            Backend = BackendName
        };
    }

    private static VisioShape? WriteGroup(VisioPage page, Group group, Page pageModel, CompatibilityReport compat)
    {
        var bounds = group.Bounds ?? EstimateBounds(group, pageModel);
        if (bounds is null)
        {
            compat.Warn("write.group-bounds", $"Group '{group.Id}' has no bounds and no members to derive bounds from.", group.Id);
            return null;
        }

        try
        {
            // Explicit bounds win: AutoResize=false keeps the author's geometry instead of
            // letting Visio recompute a bounding box that may clip members.
            var options = new VisioContainerOptions
            {
                AutoResize = group.AutoSize ?? false,
                Margin = group.Style?.PaddingX ?? 0.30,
                HeadingHeight = group.Style?.HeadingHeight ?? 0.46
            };

            var container = page.AddContainer(
                group.Id,
                MasterCatalog.Inches(bounds.X),
                MasterCatalog.Inches(bounds.Y),
                MasterCatalog.Inches(bounds.W),
                MasterCatalog.Inches(bounds.H),
                group.Name ?? group.Id,
                options);

            ApplyStyle(container, group.Style, isContainer: true);

            // Container headings sit on the top band, not the geometric centre, otherwise the
            // caption lands on top of the member shapes.
            var heading = group.Style?.HeadingHeight ?? 0.46;
            container.TextStyle ??= new VisioTextStyle();
            container.TextStyle.HorizontalAlignment = ShapeStyler.ParseAlign(group.Style?.HAlign ?? "left");
            container.TextStyle.VerticalAlignment = VisioTextVerticalAlignment.Top;
            container.TextStyle.TopMargin = 0.04;
            container.TextStyle.LeftMargin = 0.10;
            if (group.Style?.FontSize is null) container.TextStyle.Size = 12;

            return container;
        }
        catch (Exception ex)
        {
            compat.Error("write.group-failed", $"Group '{group.Id}' could not be written: {ex.Message}", group.Id);
            return null;
        }
    }

    private static Bounds? EstimateBounds(Group group, Page pageModel)
    {
        var members = pageModel.Nodes.Where(n => group.Members.Contains(n.Id)).ToList();
        if (members.Count == 0) return null;
        double minX = members.Min(m => m.Bounds.X - m.Bounds.W / 2);
        double maxX = members.Max(m => m.Bounds.X + m.Bounds.W / 2);
        double minY = members.Min(m => m.Bounds.Y - m.Bounds.H / 2);
        double maxY = members.Max(m => m.Bounds.Y + m.Bounds.H / 2);
        const double padX = 0.35, padTop = 0.42, padBottom = 0.22;
        return new Bounds(
            (minX + maxX) / 2,
            (minY + maxY) / 2 + (padTop - padBottom) / 2,
            (maxX - minX) + padX * 2,
            (maxY - minY) + padTop + padBottom);
    }

    private static VisioShape WriteNode(VisioPage page, Node node, string shapeId, Diagram diagram, CompatibilityReport compat)
    {
        var master = MasterCatalog.ResolveMaster(node.Shape);
        if (!MasterCatalog.BuiltIn.Contains(master))
            compat.Warn("write.master-unknown", $"Node '{node.Id}' shape '{node.Shape}' is not a built-in master; using Rectangle.", node.Id);

        var b = node.Bounds;
        VisioShape shape;
        try
        {
            shape = master switch
            {
                "Circle" => page.AddCircle(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(Math.Min(b.W, b.H)), node.Text),
                "Ellipse" => page.AddEllipse(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Square" => page.AddSquare(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(Math.Min(b.W, b.H)), node.Text),
                "Diamond" => page.AddDiamond(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Process" => page.AddProcess(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Decision" => page.AddDecision(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Data" => page.AddData(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Preparation" => page.AddPreparation(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Manual operation" => page.AddManualOperation(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Off-page reference" => page.AddOffPageReference(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Parallelogram" => page.AddParallelogram(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Hexagon" => page.AddHexagon(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Trapezoid" => page.AddTrapezoid(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Pentagon" => page.AddPentagon(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Triangle" => page.AddTriangle(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                "Text Box" => page.AddTextBox(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text),
                _ => page.AddRectangle(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text)
            };
        }
        catch (Exception ex)
        {
            compat.Error("write.node-failed", $"Node '{node.Id}' could not be written as '{master}': {ex.Message}. Falling back to Rectangle.", node.Id);
            shape = page.AddRectangle(shapeId, MasterCatalog.Inches(b.X), MasterCatalog.Inches(b.Y), MasterCatalog.Inches(b.W), MasterCatalog.Inches(b.H), node.Text);
        }

        if (!string.IsNullOrWhiteSpace(node.Name)) shape.Name = node.Name!;
        if (node.Angle is double angle) shape.Angle = angle;

        // Persist the stable model id so a later load can recover the agent-facing identifier
        // even when the Visio shape id differs (for example after a rename).
        shape.SetUserCell("VsdxModelId", node.Id, prompt: "Agent VSDX Engine stable node id");

        // The semantic role drives roleColors and palette lookup. Without persisting it, a model
        // read back from the file has no roles left, so it can never be re-styled — the fill
        // survives as a literal colour, which is what makes the loss easy to miss.
        if (!string.IsNullOrWhiteSpace(node.Type))
            shape.SetUserCell("VsdxRole", node.Type, prompt: "Agent VSDX Engine node role");

        ApplyStyle(shape, node.Style, isContainer: false, diagram);

        if (node.ShapeData is { Count: > 0 })
        {
            foreach (var (key, value) in node.ShapeData)
            {
                string? label = null;
                node.ShapeDataLabels?.TryGetValue(key, out label);
                shape.SetShapeData(key, value ?? "", label);
            }
        }

        if (!string.IsNullOrWhiteSpace(node.Hyperlink))
            shape.AddHyperlink(node.Hyperlink!, node.Name);

        if (node.Layers is { Count: > 0 })
            foreach (var layer in node.Layers) page.AddToLayer(layer, shape);

        return shape;
    }

    private static VisioConnector? WriteEdge(
        VisioPage page, Edge edge, Dictionary<string, VisioShape> nodeShapes,
        HashSet<string> usedIds, CompatibilityReport compat)
    {
        var connectorId = UniqueId(edge.Id, usedIds);
        usedIds.Add(connectorId);

        VisioConnector connector;
        try
        {
            if (edge.From is not null && edge.To is not null)
            {
                if (!nodeShapes.TryGetValue(edge.From, out var from))
                {
                    compat.Error("write.edge-from-missing", $"Edge '{edge.Id}' references unknown source node '{edge.From}'.", edge.Id);
                    return null;
                }
                if (!nodeShapes.TryGetValue(edge.To, out var to))
                {
                    compat.Error("write.edge-to-missing", $"Edge '{edge.Id}' references unknown target node '{edge.To}'.", edge.Id);
                    return null;
                }
                connector = page.AddConnector(
                    connectorId, from, to,
                    ParseKind(edge.Kind),
                    ParseSide(edge.FromPort),
                    ParseSide(edge.ToPort));
            }
            else if (edge.FromPoint is not null && edge.ToPoint is not null)
            {
                connector = page.AddConnector(
                    connectorId,
                    new OfficeIMO.Drawing.OfficePoint(edge.FromPoint.X, edge.FromPoint.Y),
                    new OfficeIMO.Drawing.OfficePoint(edge.ToPoint.X, edge.ToPoint.Y),
                    ParseKind(edge.Kind));
            }
            else
            {
                compat.Error("write.edge-endpoints", $"Edge '{edge.Id}' needs either both node endpoints or both free points.", edge.Id);
                return null;
            }
        }
        catch (Exception ex)
        {
            compat.Error("write.edge-failed", $"Edge '{edge.Id}' could not be written: {ex.Message}", edge.Id);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(edge.Label))
        {
            connector.SetLabel(edge.Label!);
            // Lift the label clear of the line so the stroke does not run through the text.
            var position = Math.Clamp(edge.LabelPosition ?? 0.5, 0.05, 0.95);
            connector.LabelPlacement = new VisioConnectorLabelPlacement(position, 0, 0.10)
            {
                Width = Math.Max(1.4, (edge.Label.Length * 0.11) + 0.5),
                Height = 0.24
            };
        }
        if (edge.BeginArrow is not null) connector.SetArrowStart(ParseArrow(edge.BeginArrow));
        if (edge.EndArrow is not null && !edge.EndArrow.Equals("none", StringComparison.OrdinalIgnoreCase))
            connector.SetArrowEnd(ParseArrow(edge.EndArrow));
        if (edge.LineColor is not null && ColorUtil.TryParse(edge.LineColor, out var r, out var g, out var b))
            connector.SetLineColor(OfficeColor.FromRgb(r, g, b));
        if (edge.LineWeight is double w) connector.SetLineWeight(w);
        if (edge.LinePattern is int p) connector.SetLinePattern(p);
        if (edge.LabelFont is not null) connector.SetFont(edge.LabelFont!);
        if (edge.LabelFontSize is double fs) connector.SetFontSize(fs);
        if (edge.LabelColor is not null && ColorUtil.TryParse(edge.LabelColor, out var lr, out var lg, out var lb))
            connector.SetTextColor(OfficeColor.FromRgb(lr, lg, lb));

        if (edge.Waypoints is { Count: > 0 })
        {
            foreach (var wp in edge.Waypoints)
            {
                try
                {
                    connector.Waypoints.Add(new VisioConnectorWaypoint(wp.X, wp.Y));
                }
                catch
                {
                    compat.Warn("write.edge-waypoint", $"Waypoint on edge '{edge.Id}' could not be applied.", edge.Id);
                }
            }
        }

        if (edge.Layers is { Count: > 0 })
            foreach (var layer in edge.Layers) page.AddToLayer(layer, connector);

        return connector;
    }

    private static void ApplyStyle(VisioShape shape, ShapeStyle? style, bool isContainer, Diagram? diagram = null)
    {
        var fill = ColorUtil.Normalize(style?.Fill);
        var stroke = ColorUtil.Normalize(style?.Stroke);

        if (fill is not null && ColorUtil.TryParse(fill, out var fr, out var fg, out var fb))
        {
            int pattern = 1;
            if (style?.FillPattern is not null && int.TryParse(style.FillPattern, out var fp)) pattern = fp;
            shape.SetFill(OfficeColor.FromRgb(fr, fg, fb), pattern);
        }

        if (stroke is not null && ColorUtil.TryParse(stroke, out var sr, out var sg, out var sb))
            shape.SetStroke(OfficeColor.FromRgb(sr, sg, sb), style?.StrokeWidth ?? (isContainer ? 0.014 : 0.013));
        else if (isContainer)
            shape.SetStroke(OfficeColor.FromRgb(0x92, 0xCA, 0xE0), 0.014);

        var textColor = ColorUtil.Normalize(style?.TextColor);
        if (textColor is not null && ColorUtil.TryParse(textColor, out var tr, out var tg, out var tb))
            shape.SetTextColor(OfficeColor.FromRgb(tr, tg, tb));
        else if (fill is not null && !isContainer)
            shape.SetTextColor(ColorUtil.TryParse(ColorUtil.ReadableTextColor(fill), out var rr, out var rg, out var rb)
                ? OfficeColor.FromRgb(rr, rg, rb)
                : OfficeColor.White);

        var font = style?.Font ?? diagram?.Style?.Font;
        if (!string.IsNullOrWhiteSpace(font)) shape.SetFont(font!);

        var fontSize = style?.FontSize ?? diagram?.Style?.FontSize;
        if (fontSize is double size) shape.SetFontSize(size);

        if (style?.Bold is bool bold) shape.SetBold(bold);
        if (style?.Italic is bool italic) shape.SetItalic(italic);

        if (isContainer)
        {
            shape.SetFontSize(fontSize ?? 11);
            if (style?.Bold is null) shape.SetBold(true);
        }

        shape.TextStyle ??= new VisioTextStyle();
        shape.TextStyle.HorizontalAlignment = ParseAlign(style?.HAlign);
        shape.TextStyle.VerticalAlignment = ParseVAlign(style?.VAlign);
    }

    private static OfficeIMO.OfficeAlignment ParseAlign(string? value) => value?.ToLowerInvariant() switch
    {
        "left" or "start" => OfficeIMO.OfficeAlignment.Left,
        "right" or "end" => OfficeIMO.OfficeAlignment.Right,
        _ => OfficeIMO.OfficeAlignment.Center
    };

    private static VisioTextVerticalAlignment ParseVAlign(string? value) => value?.ToLowerInvariant() switch
    {
        "top" or "start" => VisioTextVerticalAlignment.Top,
        "bottom" or "end" => VisioTextVerticalAlignment.Bottom,
        _ => VisioTextVerticalAlignment.Middle
    };

    private static ConnectorKind ParseKind(string? kind) => kind?.ToLowerInvariant() switch
    {
        "straight" => ConnectorKind.Straight,
        "rightangle" or "right-angle" or "orthogonal" or "elbow" => ConnectorKind.RightAngle,
        "curved" or "curve" => ConnectorKind.Curved,
        _ => ConnectorKind.Dynamic
    };

    private static VisioSide ParseSide(string? side) => side?.ToLowerInvariant() switch
    {
        "left" => VisioSide.Left,
        "right" => VisioSide.Right,
        "top" => VisioSide.Top,
        "bottom" => VisioSide.Bottom,
        _ => VisioSide.Auto
    };

    private static EndArrow ParseArrow(string? arrow) => arrow?.ToLowerInvariant() switch
    {
        "arrow" => EndArrow.Arrow,
        "triangle" => EndArrow.Triangle,
        _ => EndArrow.None
    };

    private static string UniqueId(string desired, HashSet<string> used)
    {
        if (string.IsNullOrWhiteSpace(desired)) desired = "s";
        var candidate = desired;
        int suffix = 1;
        while (used.Contains(candidate)) candidate = $"{desired}-{suffix++}";
        return candidate;
    }

    /// <summary>
    /// Names the style fields the model and schema accept but this writer cannot express.
    ///
    /// Opacity, shadow, radius, lineJoin and paddingBottom are declared in
    /// `schemas/diagram.schema.json` and have properties on <see cref="ShapeStyle"/>, yet nothing
    /// downstream reads them. Without this scan a caller can set all five, be told the write was
    /// lossless, and receive a file where none of them took effect — accepted by the protocol,
    /// permitted by the schema, ignored at execution, and silent.
    ///
    /// Reported per field rather than as one lump: "a style was dropped" does not tell the caller
    /// what to remove or work around.
    /// </summary>
    private static void ReportUnsupportedStyleFields(Diagram diagram, CompatibilityReport compat)
    {
        var dropped = new SortedSet<string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        void Inspect(ShapeStyle? style, string owner)
        {
            if (style is null) return;

            if (style.Opacity is not null) Note("opacity");
            if (style.Shadow is not null) Note("shadow");
            if (style.Radius is not null) Note("radius");
            if (style.LineJoin is not null) Note("lineJoin");
            if (style.PaddingBottom is not null) Note("paddingBottom");

            void Note(string field)
            {
                dropped.Add(field);
                owners.TryAdd(field, owner);
            }
        }

        foreach (var page in diagram.Pages)
        {
            foreach (var node in page.Nodes) Inspect(node.Style, node.Id);
            foreach (var group in page.Groups) Inspect(group.Style, group.Id);
            foreach (var path in page.Paths) Inspect(path.Style, path.Id);
            foreach (var text in page.TextBlocks) Inspect(text.Style, text.Id);
        }

        if (dropped.Count == 0) return;

        compat.Warn(
            "style.unsupported",
            $"以下样式字段已接受但当前无法写入 VSDX，将被丢弃：{string.Join(", ", dropped)}",
            owners[dropped.First()]);
    }
}
