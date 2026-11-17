using OfficeIMO;
using OfficeIMO.Visio;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Applies a JSON model onto an existing VSDX package without regenerating it.
///
/// The package is loaded once; only objects whose model representation differs from the
/// package are touched. Everything else — including shapes the model never mentions and
/// native masters, protection, hyperlinks and StyleSheet references — is written back
/// exactly as it was read.
/// </summary>
public sealed class OfficeImoIncrementalEditor : IIncrementalDocumentEditor
{
    public const string BackendName = "OfficeIMO.Visio";

    public UpdateResult Update(string sourceVsdxPath, Diagram model, string outputVsdxPath, UpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new UpdateOptions();

        if (!File.Exists(sourceVsdxPath))
            throw new FileNotFoundException($"Source VSDX not found: {sourceVsdxPath}", sourceVsdxPath);

        DiagramJson.Normalize(model);
        var compat = new CompatibilityReport();

        var reader = new OfficeImoDocumentReader();
        var baseline = reader.Read(sourceVsdxPath);
        compat.Merge(baseline.Compatibility);

        // P0: remember which shapes carry no geometry, so the sections the backend synthesises
        // during Save can be removed again. Must happen before Load.
        var ungeometried = VisioGeometryPreserver.SnapshotUngeometriedShapes(sourceVsdxPath);

        var doc = VisioDocument.Load(sourceVsdxPath);

        var changes = new List<ModelChange>();
        int added = 0, modified = 0, deleted = 0, preserved = 0;
        int edgesAdded = 0, edgesModified = 0, edgesDeleted = 0;

        foreach (var pageModel in model.Pages)
        {
            var page = FindPage(doc, pageModel, compat);
            if (page is null)
            {
                compat.Error("update.page-missing", $"Page '{pageModel.Name}' not found in the source package.", pageModel.Name);
                continue;
            }

            // Page geometry is part of the model: apply size changes to the existing page.
            if (pageModel.Size is not null)
            {
                double targetW = pageModel.Size.Width, targetH = pageModel.Size.Height;
                double factor = MasterCatalog.UnitFactor(pageModel.Size.Unit);
                targetW *= factor; targetH *= factor;
                double beforeW = page.Width.ToInches(), beforeH = page.Height.ToInches();
                if (Math.Abs(beforeW - targetW) > 1e-6 || Math.Abs(beforeH - targetH) > 1e-6)
                {
                    page.Width = MasterCatalog.Inches(targetW);
                    page.Height = MasterCatalog.Inches(targetH);
                    changes.Add(new ModelChange
                    {
                        Kind = "page.resized",
                        Target = page.Name,
                        Before = $"{beforeW:F2}x{beforeH:F2}",
                        After = $"{targetW:F2}x{targetH:F2}"
                    });
                }
            }

            var existingShapes = new Dictionary<string, VisioShape>(StringComparer.Ordinal);
            foreach (var shape in page.Shapes) existingShapes[shape.Id] = shape;

            var existingConnectors = new Dictionary<string, VisioConnector>(StringComparer.Ordinal);
            foreach (var connector in page.Connectors) existingConnectors[connector.Id] = connector;

            var baselinePage = baseline.Diagram.Pages.FirstOrDefault(p => p.Name == page.Name)
                            ?? baseline.Diagram.Pages.FirstOrDefault();

            // ---- Update existing nodes, and add new ones -------------------
            var seenNodeIds = new HashSet<string>(StringComparer.Ordinal);   // model ids
            var ownedShapeIds = new HashSet<string>(StringComparer.Ordinal); // Visio shape ids
            foreach (var node in pageModel.Nodes)
            {
                seenNodeIds.Add(node.Id);

                // Match priority: model id -> preserved Visio shape id -> visible text.
                // The Visio-id fallback is what makes a rename update the existing shape in
                // place instead of creating a duplicate and dropping the original.
                VisioShape? shape = null;
                if (existingShapes.TryGetValue(node.Id, out var direct)) shape = direct;

                if (shape is null && !string.IsNullOrWhiteSpace(node.VisioShapeId) &&
                    existingShapes.TryGetValue(node.VisioShapeId!, out var byVisioId))
                {
                    shape = byVisioId;
                }

                if (shape is null && !string.IsNullOrWhiteSpace(node.Text))
                {
                    shape = existingShapes.Values.FirstOrDefault(s =>
                        string.Equals(s.Text, node.Text, StringComparison.Ordinal));
                    if (shape is not null)
                        compat.Info("update.matched-by-text",
                            $"Node '{node.Id}' matched shape '{shape.Id}' by its text.", node.Id);
                }

                if (shape is not null) ownedShapeIds.Add(shape.Id);

                if (shape is null)
                {
                    var created = CreateShape(page, node, existingShapes.Keys.ToHashSet(StringComparer.Ordinal), model, compat);
                    if (created is null) continue;
                    existingShapes[created.Id] = created;
                    ownedShapeIds.Add(created.Id);
                    node.VisioShapeId = created.Id;
                    added++;
                    changes.Add(new ModelChange { Kind = "node.added", Target = node.Id, Detail = node.Text });
                    continue;
                }

                var beforeSnapshot = SnapshotShape(shape);
                bool changed = ApplyNodeChanges(shape, node, model, compat);
                var afterSnapshot = SnapshotShape(shape);
                if (changed)
                {
                    modified++;
                    changes.Add(new ModelChange
                    {
                        Kind = "node.modified",
                        Target = node.Id,
                        Before = beforeSnapshot,
                        After = afterSnapshot
                    });
                }
                else
                {
                    preserved++;
                }

                // The stored Visio id may differ from the model id (rename case). Alias the new
                // id onto the same shape so edge lookups resolve...
                if (!string.Equals(shape.Id, node.Id, StringComparison.Ordinal))
                {
                    existingShapes[node.Id] = shape;
                    _idAliases[shape.Id] = node.Id;
                }
                _idAliases[shape.Id] = node.Id;
            }

            // ---- Deletions ------------------------------------------------
            foreach (var shape in existingShapes.Values.Distinct().ToList())
            {
                if (ownedShapeIds.Contains(shape.Id)) continue;
                if (OfficeImoDocumentReader.IsContainerShape(shape)) continue;
                if (baselinePage is not null && !baselinePage.Nodes.Any(n => n.Id == shape.Id))
                {
                    // Never seen by the model: keep it unless delete-missing was requested.
                    if (!options.DeleteMissing)
                    {
                        preserved++;
                        compat.Info("update.retained-unmodelled",
                            $"Shape '{shape.Id}' exists in the package but not in the model; retained unmodified.", shape.Id);
                        continue;
                    }
                }
                if (!options.DeleteMissing) continue;

                page.Shapes.Remove(shape);
                deleted++;
                changes.Add(new ModelChange { Kind = "node.deleted", Target = shape.Id, Before = SnapshotShape(shape) });
            }

            // ---- Update existing edges, and add new ones -------------------
            var seenEdgeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edge in pageModel.Edges)
            {
                seenEdgeIds.Add(edge.Id);
                if (existingConnectors.TryGetValue(edge.Id, out var connector))
                {
                    bool changed = ApplyEdgeChanges(page, connector, edge, existingShapes, compat);
                    if (changed)
                    {
                        edgesModified++;
                        changes.Add(new ModelChange { Kind = "edge.modified", Target = edge.Id, After = edge.Label });
                    }
                    continue;
                }

                // Match a connector that was renamed while keeping its endpoints, or that a
                // previous version of this model wrote under a different id. Without this the
                // package ends up with both the old and the new connector.
                var sameEndpoints = existingConnectors.Values.FirstOrDefault(c =>
                    string.Equals(ResolveModelId(c.From?.Id), edge.From, StringComparison.Ordinal) &&
                    string.Equals(ResolveModelId(c.To?.Id), edge.To, StringComparison.Ordinal) &&
                    !seenEdgeIds.Contains(c.Id));

                if (sameEndpoints is not null)
                {
                    existingConnectors[edge.Id] = sameEndpoints;
                    seenEdgeIds.Add(sameEndpoints.Id);
                    bool renamed = true;
                    if (!string.IsNullOrWhiteSpace(edge.Label))
                    {
                        sameEndpoints.SetLabel(edge.Label!);
                        renamed = true;
                    }
                    if (renamed)
                    {
                        edgesModified++;
                        changes.Add(new ModelChange
                        {
                            Kind = "edge.modified",
                            Target = edge.Id,
                            Detail = $"matched existing connector '{sameEndpoints.Id}' by endpoints"
                        });
                    }
                    if (edge.Waypoints is { Count: > 0 }) ApplyWaypoints(sameEndpoints, edge, compat);
                    continue;
                }

                var created = CreateConnector(page, edge, existingShapes, existingConnectors.Keys.ToHashSet(StringComparer.Ordinal), compat);
                if (created is null) continue;
                existingConnectors[created.Id] = created;
                edge.VisioShapeId = created.Id;
                edgesAdded++;
                changes.Add(new ModelChange { Kind = "edge.added", Target = edge.Id, Detail = $"{edge.From} -> {edge.To}" });
            }

            if (options.DeleteMissing)
            {
                // Match by endpoint pair, resolved through the rename alias map, so a connector
                // that the model still describes under a different id is never deleted.
                var modelEndpoints = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in pageModel.Edges)
                    modelEndpoints.Add($"{e.From}|{e.To}");

                foreach (var connector in page.Connectors.ToList())
                {
                    var pair = $"{ResolveModelId(connector.From?.Id)}|{ResolveModelId(connector.To?.Id)}";
                    if (modelEndpoints.Contains(pair)) continue;

                    // Unknown to the model: keep it unless the caller asked for pruning.
                    if (baselinePage is not null &&
                        baselinePage.Edges.All(e => $"{e.From}|{e.To}" != pair))
                    {
                        preserved++;
                        compat.Info("update.retained-unmodelled-edge",
                            $"Connector '{connector.Id}' exists in the package but not in the model; retained.", connector.Id);
                        continue;
                    }

                    page.Connectors.Remove(connector);
                    edgesDeleted++;
                    changes.Add(new ModelChange { Kind = "edge.deleted", Target = connector.Id });
                }
            }

            // ---- Groups ---------------------------------------------------
            ApplyGroups(page, pageModel, existingShapes, compat, changes);

            // ---- Layers ---------------------------------------------------
            ApplyLayers(page, pageModel);
        }

        if (options.RecordHistory)
        {
            model.Metadata.History.Add(new ChangeRecord
            {
                At = DateTimeOffset.UtcNow,
                Action = "update",
                Target = Path.GetFileName(outputVsdxPath),
                Detail = $"+{added} ~{modified} -{deleted} nodes; +{edgesAdded} ~{edgesModified} -{edgesDeleted} edges; {preserved} preserved",
                Tool = $"{EngineInfo.Name} {EngineInfo.Version}"
            });
            model.Metadata.Modified = DateTimeOffset.UtcNow;
            model.Metadata.SourceFile = Path.GetFullPath(sourceVsdxPath);
        }

        if (options.ValidateAfterSave) doc.RequestRecalcOnOpen();
        doc.Save(outputVsdxPath);

        // P0: the backend synthesises a rectangle for any shape that had no geometry. Strip those
        // so an unmodified shape keeps whatever its master (or its absence) says it looks like.
        VisioGeometryPreserver.RestoreAfterSave(outputVsdxPath, ungeometried, compat);

        // Custom geometry has no representation in the incremental API, so paths are written
        // straight into the page part, exactly as on create.
        VisioFreePathBridge.Apply(model, outputVsdxPath, compat);

        // Z-order is document order in Visio; restore the author's stacking after the writer has
        // created shapes in its own dependency-satisfying order.
        VisioZOrderBridge.Apply(model, outputVsdxPath, compat);

        VisioMetadataBridge.Write(model, outputVsdxPath);

        var result = new UpdateResult
        {
            OutputPath = Path.GetFullPath(outputVsdxPath),
            Compatibility = compat,
            Changes = changes,
            NodesAdded = added,
            NodesModified = modified,
            NodesDeleted = deleted,
            EdgesAdded = edgesAdded,
            EdgesModified = edgesModified,
            EdgesDeleted = edgesDeleted,
            NodesPreserved = preserved,
            FileSize = new FileInfo(outputVsdxPath).Length,
            Backend = BackendName
        };

        return result;
    }

    private static OfficeIMO.Visio.VisioPage? FindPage(VisioDocument doc, Page pageModel, CompatibilityReport compat)
    {
        var page = doc.Pages.FirstOrDefault(p => string.Equals(p.Name, pageModel.Name, StringComparison.OrdinalIgnoreCase));
        if (page is not null) return page;
        if (pageModel.VisioId is int id)
        {
            page = doc.Pages.FirstOrDefault(p => p.Id == id);
            if (page is not null) return page;
        }
        var byId = doc.Pages.FirstOrDefault(p => string.Equals($"page-{p.Id}", pageModel.Id, StringComparison.OrdinalIgnoreCase));
        if (byId is not null) return byId;
        compat.Warn("update.page-fallback", $"Page '{pageModel.Name}' not matched by name/id; falling back to first page.", pageModel.Name);
        return doc.Pages.FirstOrDefault();
    }

    private static VisioShape? CreateShape(
        VisioPage page, Node node, HashSet<string> usedIds, Diagram diagram, CompatibilityReport compat)
    {
        var shapeId = node.Id;
        int suffix = 1;
        while (usedIds.Contains(shapeId)) shapeId = $"{node.Id}-{suffix++}";

        var bounds = node.Bounds;
        var master = MasterCatalog.ResolveMaster(node.Shape);
        VisioShape shape;
        try
        {
            shape = master switch
            {
                "Circle" => page.AddCircle(shapeId, MasterCatalog.Inches(bounds.X), MasterCatalog.Inches(bounds.Y), MasterCatalog.Inches(Math.Min(bounds.W, bounds.H)), node.Text),
                "Ellipse" => page.AddEllipse(shapeId, MasterCatalog.Inches(bounds.X), MasterCatalog.Inches(bounds.Y), MasterCatalog.Inches(bounds.W), MasterCatalog.Inches(bounds.H), node.Text),
                "Diamond" or "Decision" => page.AddDiamond(shapeId, MasterCatalog.Inches(bounds.X), MasterCatalog.Inches(bounds.Y), MasterCatalog.Inches(bounds.W), MasterCatalog.Inches(bounds.H), node.Text),
                "Process" => page.AddProcess(shapeId, MasterCatalog.Inches(bounds.X), MasterCatalog.Inches(bounds.Y), MasterCatalog.Inches(bounds.W), MasterCatalog.Inches(bounds.H), node.Text),
                "Hexagon" => page.AddHexagon(shapeId, MasterCatalog.Inches(bounds.X), MasterCatalog.Inches(bounds.Y), MasterCatalog.Inches(bounds.W), MasterCatalog.Inches(bounds.H), node.Text),
                "Text Box" => page.AddTextBox(shapeId, MasterCatalog.Inches(bounds.X), MasterCatalog.Inches(bounds.Y), MasterCatalog.Inches(bounds.W), MasterCatalog.Inches(bounds.H), node.Text),
                _ => page.AddRectangle(shapeId, MasterCatalog.Inches(bounds.X), MasterCatalog.Inches(bounds.Y), MasterCatalog.Inches(bounds.W), MasterCatalog.Inches(bounds.H), node.Text)
            };
        }
        catch (Exception ex)
        {
            compat.Error("update.add-failed", $"Node '{node.Id}' could not be added: {ex.Message}", node.Id);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(node.Name)) shape.Name = node.Name!;
        shape.SetUserCell("VsdxModelId", node.Id, prompt: "Agent VSDX Engine stable node id");
        ShapeStyler.Apply(shape, node.Style, diagram);
        if (node.ShapeData is { Count: > 0 })
            foreach (var (key, value) in node.ShapeData)
                shape.SetShapeData(key, value ?? "", node.ShapeDataLabels?.GetValueOrDefault(key));
        return shape;
    }

    private static VisioConnector? CreateConnector(
        VisioPage page, Edge edge, Dictionary<string, VisioShape> shapes,
        HashSet<string> usedIds, CompatibilityReport compat)
    {
        if (edge.From is null || edge.To is null)
        {
            compat.Warn("update.edge-needs-nodes", $"New edge '{edge.Id}' is not attached to nodes and cannot be added incrementally.", edge.Id);
            return null;
        }
        if (!shapes.TryGetValue(edge.From, out var from))
        {
            compat.Error("update.edge-from-missing", $"Edge '{edge.Id}' references missing source node '{edge.From}'.", edge.Id);
            return null;
        }
        if (!shapes.TryGetValue(edge.To, out var to))
        {
            compat.Error("update.edge-to-missing", $"Edge '{edge.Id}' references missing target node '{edge.To}'.", edge.Id);
            return null;
        }

        var id = edge.Id;
        int suffix = 1;
        while (usedIds.Contains(id)) id = $"{edge.Id}-{suffix++}";

        try
        {
            var connector = page.AddConnector(id, from, to,
                ParseKind(edge.Kind), ParseSide(edge.FromPort), ParseSide(edge.ToPort));
            ApplyWaypoints(connector, edge, compat);
            if (!string.IsNullOrWhiteSpace(edge.Label)) connector.SetLabel(edge.Label!);
            if (edge.EndArrow is not null) connector.SetArrowEnd(ParseArrow(edge.EndArrow));
            if (edge.LineColor is not null && ColorUtil.TryParse(edge.LineColor, out var r, out var g, out var b))
                connector.SetLineColor(OfficeColor.FromRgb(r, g, b));
            return connector;
        }
        catch (Exception ex)
        {
            compat.Error("update.edge-failed", $"Edge '{edge.Id}' could not be added: {ex.Message}", edge.Id);
            return null;
        }
    }

    /// <summary>Applies only the properties that differ. Returns true when anything changed.</summary>
    private static bool ApplyNodeChanges(VisioShape shape, Node node, Diagram diagram, CompatibilityReport compat)
    {
        bool changed = false;

        if (node.Text is not null && !string.Equals(shape.Text, node.Text, StringComparison.Ordinal))
        {
            shape.SetText(node.Text);
            changed = true;
        }

        // Only rename when the package actually carries a different non-empty name; normalization
        // fills node.Name with the node id, which must not count as a change.
        if (!string.IsNullOrWhiteSpace(node.Name) &&
            !string.Equals(shape.Name, node.Name, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(shape.Name))
        {
            shape.Name = node.Name;
            changed = true;
        }
        else if (string.IsNullOrWhiteSpace(shape.Name) && !string.IsNullOrWhiteSpace(node.Name))
        {
            // Fill in a missing shape name (common in externally authored files) without
            // reporting a modification the user did not ask for.
            shape.Name = node.Name;
        }

        // Keep the engine-stamped stable id in sync so a renamed node keeps its identity.
        // Stamping is bookkeeping, so it never counts as a user-visible modification.
        var storedId = shape.GetUserCellValue("VsdxModelId");
        if (!string.Equals(storedId, node.Id, StringComparison.Ordinal))
            shape.SetUserCell("VsdxModelId", node.Id, prompt: "Agent VSDX Engine stable node id");

        var b = node.Bounds;
        if (Math.Abs(shape.PinX.ToInches() - b.X) > 1e-6 ||
            Math.Abs(shape.PinY.ToInches() - b.Y) > 1e-6 ||
            Math.Abs(shape.Width.ToInches() - b.W) > 1e-6 ||
            Math.Abs(shape.Height.ToInches() - b.H) > 1e-6)
        {
            shape.PinX = MasterCatalog.Inches(b.X);
            shape.PinY = MasterCatalog.Inches(b.Y);
            shape.Width = MasterCatalog.Inches(b.W);
            shape.Height = MasterCatalog.Inches(b.H);
            changed = true;
        }

        if (node.Angle is double angle && Math.Abs(shape.Angle - angle) > 1e-9)
        {
            shape.Angle = angle;
            changed = true;
        }

        if (node.Style is not null && ShapeStyler.ApplyChanges(shape, node.Style, compat, node.Id))
            changed = true;

        if (node.ShapeData is { Count: > 0 })
        {
            foreach (var (key, value) in node.ShapeData)
            {
                var existing = shape.GetShapeDataValue(key);
                if (!string.Equals(existing, value, StringComparison.Ordinal))
                {
                    shape.SetShapeData(key, value ?? "", node.ShapeDataLabels?.GetValueOrDefault(key));
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool ApplyEdgeChanges(
        VisioPage? page, VisioConnector connector, Edge edge, Dictionary<string, VisioShape> shapes, CompatibilityReport compat)
    {
        bool changed = false;

        if (edge.Label is not null && !string.Equals(connector.Label, edge.Label, StringComparison.Ordinal))
        {
            connector.SetLabel(edge.Label);
            changed = true;
        }

        // Reattach when the model moved one endpoint to a different node.
        if (edge.From is not null && edge.To is not null)
        {
            bool fromDiffers = !string.Equals(connector.From?.Id, edge.From, StringComparison.Ordinal);
            bool toDiffers = !string.Equals(connector.To?.Id, edge.To, StringComparison.Ordinal);
            if (fromDiffers || toDiffers)
            {
                if (shapes.TryGetValue(edge.From, out var from) && shapes.TryGetValue(edge.To, out var to))
                {
                    // Use the supported reconnect API so endpoint ShapeSheet cells stay consistent.
                    page!.ReconnectConnector(connector, from, to, ParseSide(edge.FromPort), ParseSide(edge.ToPort));
                    changed = true;
                }
                else
                {
                    compat.Warn("update.edge-endpoint-missing", $"Edge '{edge.Id}' endpoint not found; reconnect skipped.", edge.Id);
                }
            }
        }

        if (edge.LineColor is not null && ColorUtil.TryParse(edge.LineColor, out var r, out var g, out var b))
        {
            var current = OfficeImoDocumentReader.FormatColor(connector.LineColor);
            var target = ColorUtil.ToHex(r, g, b);
            if (!string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
            {
                connector.SetLineColor(OfficeColor.FromRgb(r, g, b));
                changed = true;
            }
        }

        if (edge.EndArrow is not null && !string.Equals(connector.EndArrow.ToString(), edge.EndArrow, StringComparison.OrdinalIgnoreCase))
        {
            connector.SetArrowEnd(ParseArrow(edge.EndArrow));
            changed = true;
        }

        if (edge.Waypoints is { Count: > 0 } && WaypointsDiffer(connector, edge))
        {
            ApplyWaypoints(connector, edge, compat);
            changed = true;
        }

        return changed;
    }

    private static void ApplyGroups(
        VisioPage page, Page pageModel, Dictionary<string, VisioShape> shapes,
        CompatibilityReport compat, List<ModelChange> changes)
    {
        foreach (var group in pageModel.Groups)
        {
            // Rename an existing container in place rather than recreating it.
            var container = page.Shapes.FirstOrDefault(s => s.Id == (group.VisioShapeId ?? group.Id))
                         ?? page.Shapes.FirstOrDefault(s => OfficeImoDocumentReader.IsContainerShape(s)
                                                       && string.Equals(s.Text, group.Name, StringComparison.Ordinal));

            if (container is not null && group.Name is not null &&
                !string.Equals(container.Text, group.Name, StringComparison.Ordinal))
            {
                var before = container.Text;
                container.SetText(group.Name);
                changes.Add(new ModelChange { Kind = "group.renamed", Target = group.Id, Before = before, After = group.Name });
            }

            if (group.Kind == "group" && container is null) continue;

            // Re-assign membership for members that exist.
            var memberShapeIds = group.Members
                .Where(shapes.ContainsKey)
                .Select(m => shapes[m].Id)
                .ToList();
            if (memberShapeIds.Count == 0) continue;
            if (container is null) continue;
            try
            {
                page.AddToContainer(container.Id, memberShapeIds, resizeToFit: group.AutoSize ?? true);
            }
            catch (Exception ex)
            {
                compat.Warn("update.container", $"Group '{group.Id}' membership could not be applied: {ex.Message}", group.Id);
            }
        }
    }

    private static void ApplyLayers(VisioPage page, Page pageModel)
    {
        foreach (var layer in pageModel.Layers)
        {
            var existing = page.Layers.FirstOrDefault(l =>
                string.Equals(l.Name, layer.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = page.AddLayer(layer.Name, layer.NameU);
            }
            existing.Visible = layer.Visible;
            existing.Lock = layer.Locked;
            existing.Print = layer.Print;

            foreach (var member in layer.Members)
            {
                var shape = page.Shapes.FirstOrDefault(s => s.Id == member);
                if (shape is not null) page.AddToLayer(layer.Name, shape);
                else
                {
                    var conn = page.Connectors.FirstOrDefault(c => c.Id == member);
                    if (conn is not null) page.AddToLayer(layer.Name, conn);
                }
            }
        }
    }

    /// <summary>Maps a Visio shape id back to the stable model id the agent used.</summary>
    private string ResolveModelId(string? visioShapeId)
    {
        if (visioShapeId is null) return "";
        return _idAliases.TryGetValue(visioShapeId, out var modelId) ? modelId : visioShapeId;
    }

    private readonly Dictionary<string, string> _idAliases = new(StringComparer.Ordinal);

    private static bool WaypointsDiffer(VisioConnector connector, Edge edge)
    {
        if (edge.Waypoints is null) return false;
        if (connector.Waypoints.Count != edge.Waypoints.Count) return true;
        for (int i = 0; i < edge.Waypoints.Count; i++)
        {
            if (Math.Abs(connector.Waypoints[i].X - edge.Waypoints[i].X) > 1e-6 ||
                Math.Abs(connector.Waypoints[i].Y - edge.Waypoints[i].Y) > 1e-6)
                return true;
        }
        return false;
    }

    private static void ApplyWaypoints(VisioConnector connector, Edge edge, CompatibilityReport compat)
    {
        if (edge.Waypoints is not { Count: > 0 }) return;
        connector.Waypoints.Clear();
        foreach (var wp in edge.Waypoints)
        {
            try { connector.Waypoints.Add(new VisioConnectorWaypoint(wp.X, wp.Y)); }
            catch (Exception ex)
            {
                compat.Warn("update.waypoint", $"Waypoint on edge '{edge.Id}' was rejected: {ex.Message}", edge.Id);
            }
        }
    }

    private static string SnapshotShape(VisioShape shape) =>
        $"text='{shape.Text}' pin=({shape.PinX.ToInches():F3},{shape.PinY.ToInches():F3}) " +
        $"size=({shape.Width.ToInches():F3}x{shape.Height.ToInches():F3}) fill={OfficeImoDocumentReader.FormatColor(shape.FillColor)}";

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
}
