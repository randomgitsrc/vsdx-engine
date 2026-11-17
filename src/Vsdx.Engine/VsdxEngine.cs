using Vsdx.Adapters;
using Vsdx.Model;
using Vsdx.Renderer;
using Vsdx.Validator;

namespace Vsdx.Engine;

/// <summary>
/// Reusable entry point for every VSDX operation. The CLI is a thin wrapper over this class,
/// so all behaviour is available from C# without shelling out.
/// </summary>
public sealed class VsdxEngine
{
    private readonly IDocumentReader _reader;
    private readonly IDocumentWriter _writer;
    private readonly IIncrementalDocumentEditor _editor;
    private readonly IDiagramRenderer _renderer;
    private readonly IDocumentValidator _validator;

    public VsdxEngine(
        IDocumentReader? reader = null,
        IDocumentWriter? writer = null,
        IIncrementalDocumentEditor? editor = null,
        IDiagramRenderer? renderer = null,
        IDocumentValidator? validator = null)
    {
        _reader = reader ?? new OfficeImoDocumentReader();
        _writer = writer ?? new OfficeImoDocumentWriter();
        _editor = editor ?? new OfficeImoIncrementalEditor();
        _renderer = renderer ?? new OfficeImoVsdxRenderer();
        _validator = validator ?? new VsdxPackageValidatorService();
    }

    /// <summary>Creates a VSDX from a JSON model file.</summary>
    public WriteResult Create(string modelPath, string outputPath, WriteOptions? options = null)
    {
        var diagram = DiagramJson.Load(modelPath);
        return Create(diagram, outputPath, options);
    }

    /// <summary>Creates a VSDX from an in-memory model.</summary>
    public WriteResult Create(Diagram diagram, string outputPath, WriteOptions? options = null)
    {
        DiagramJson.Normalize(diagram);

        // Text first, then containers, then the page, then connectors. Geometry flows from
        // content outward; nothing is sized independently of the words it must hold.
        if (options?.FitToText ?? false) DiagramSizer.Apply(diagram);

        if (options?.Recenter ?? false)
            foreach (var page in diagram.Pages) DiagramSizer.Recenter(page);

        // "Fit the page to the content" means both directions: a page far larger than the
        // drawing leaves the diagram looking stranded in a corner. The low-level API stays
        // grow-only by default; the pipeline opts into trimming.
        if (options?.FitPage ?? false)
            foreach (var page in diagram.Pages) DiagramSizer.FitPage(page, shrink: true);

        // Placement. "auto" resolves the diagram type to a strategy, which is what makes
        // different diagram families get genuinely different treatment.
        var requested = options?.Layout;
        if (!string.IsNullOrWhiteSpace(requested) && !requested.Equals("manual", StringComparison.OrdinalIgnoreCase))
        {
            var strategy = requested.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? DiagramLayoutEngine.SuggestStrategy(diagram)
                : DiagramLayoutEngine.Parse(requested);

            DiagramLayoutEngine.Apply(diagram, new LayoutOptions(), strategy);
        }

        // Sizing moved shapes, so container bounds and routes must be recomputed afterwards.
        if (options?.AutoLayout ?? true)
        {
            DiagramLayout.Apply(diagram);
            OrthogonalRouter.Route(diagram);
        }

        Stamp(diagram, "create", outputPath);
        var result = _writer.Write(diagram, outputPath, options);

        if (options?.ValidateAfterSave ?? true)
        {
            var validation = _validator.Validate(outputPath);
            foreach (var error in validation.Errors)
                result.Compatibility.Error("post-save." + error.Code, error.Message, error.Target);
        }

        return result;
    }

    /// <summary>Reads a VSDX into the JSON model.</summary>
    public ReadResult Read(string vsdxPath) => _reader.Read(vsdxPath);

    /// <summary>Exports a VSDX to a JSON model file.</summary>
    public ReadResult ExportModel(string vsdxPath, string modelPath)
    {
        var result = _reader.Read(vsdxPath);
        DiagramJson.Save(result.Diagram, modelPath);
        return result;
    }

    /// <summary>Applies a JSON model onto an existing VSDX (incremental update).</summary>
    public UpdateResult Update(string sourceVsdxPath, string modelPath, string outputPath, UpdateOptions? options = null)
    {
        var diagram = DiagramJson.Load(modelPath);
        return Update(sourceVsdxPath, diagram, outputPath, options);
    }

    public UpdateResult Update(string sourceVsdxPath, Diagram diagram, string outputPath, UpdateOptions? options = null)
    {
        DiagramJson.Normalize(diagram);
        if (options?.AutoLayout ?? true)
        {
            DiagramLayout.Apply(diagram);
            OrthogonalRouter.Route(diagram);
        }
        var result = _editor.Update(sourceVsdxPath, diagram, outputPath, options);

        if (options?.ValidateAfterSave ?? true)
        {
            var validation = _validator.Validate(outputPath);
            foreach (var error in validation.Errors)
                result.Compatibility.Error("post-save." + error.Code, error.Message, error.Target);
        }

        return result;
    }

    public RenderResult Render(RenderRequest request) => _renderer.Render(request);

    public ValidationResult Validate(string vsdxPath, ValidationOptions? options = null) =>
        _validator.Validate(vsdxPath, options);

    /// <summary>Compares two VSDX packages by their exported models.</summary>
    public DiagramDiff Diff(string beforePath, string afterPath)
    {
        var before = _reader.Read(beforePath);
        var after = _reader.Read(afterPath);
        return DiagramDiff.Compute(before.Diagram, after.Diagram, beforePath, afterPath);
    }

    /// <summary>Reads a VSDX and reports basic statistics without writing anything.</summary>
    public InspectResult Inspect(string vsdxPath)
    {
        var read = _reader.Read(vsdxPath);
        var validation = _validator.Validate(vsdxPath, new ValidationOptions { Deep = true });
        return new InspectResult { Read = read, Validation = validation };
    }

    public string Backend => _writer.GetType().Name;

    private static void Stamp(Diagram diagram, string action, string target)
    {
        diagram.Metadata.Modified = DateTimeOffset.UtcNow;
        diagram.Metadata.Generator = $"{EngineInfo.Name} {EngineInfo.Version}";
        diagram.Metadata.Backend = OfficeImoDocumentWriter.BackendName;
        diagram.Metadata.BackendVersion = OfficeImoDocumentReader.BackendVersion;
        diagram.Metadata.History.Add(new ChangeRecord
        {
            At = DateTimeOffset.UtcNow,
            Action = action,
            Target = Path.GetFileName(target),
            Detail = $"pages={diagram.Pages.Count}, nodes={diagram.Pages.Sum(p => p.Nodes.Count)}, edges={diagram.Pages.Sum(p => p.Edges.Count)}",
            Tool = $"{EngineInfo.Name} {EngineInfo.Version}"
        });
    }
}

public sealed class InspectResult
{
    public required ReadResult Read { get; init; }
    public required ValidationResult Validation { get; init; }
}

/// <summary>Structural difference between two diagram models.</summary>
public sealed class DiagramDiff
{
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public List<DiffEntry> Changes { get; } = new();
    public bool Identical => Changes.Count == 0;

    public IEnumerable<DiffEntry> Added => Changes.Where(c => c.Kind is "node.added" or "edge.added" or "group.added" or "layer.added" or "page.added");
    public IEnumerable<DiffEntry> Removed => Changes.Where(c => c.Kind is "node.removed" or "edge.removed" or "group.removed" or "layer.removed" or "page.removed");
    public IEnumerable<DiffEntry> Modified => Changes.Where(c => c.Kind.EndsWith(".modified", StringComparison.Ordinal));

    public static DiagramDiff Compute(Diagram before, Diagram after, string beforePath, string afterPath)
    {
        var diff = new DiagramDiff { Before = beforePath, After = afterPath };

        var beforePages = before.Pages.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var afterPages = after.Pages.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var name in beforePages.Keys)
            if (!afterPages.ContainsKey(name))
                diff.Changes.Add(new DiffEntry { Kind = "page.removed", Target = name });

        foreach (var name in afterPages.Keys)
            if (!beforePages.ContainsKey(name))
                diff.Changes.Add(new DiffEntry { Kind = "page.added", Target = name });

        foreach (var (name, beforePage) in beforePages)
        {
            if (!afterPages.TryGetValue(name, out var afterPage)) continue;
            DiffPage(diff, beforePage, afterPage);
        }

        return diff;
    }

    private static void DiffPage(DiagramDiff diff, Page before, Page after)
    {
        if (Math.Abs((before.Size?.Width ?? 0) - (after.Size?.Width ?? 0)) > 1e-6 ||
            Math.Abs((before.Size?.Height ?? 0) - (after.Size?.Height ?? 0)) > 1e-6)
        {
            diff.Changes.Add(new DiffEntry
            {
                Kind = "page.modified",
                Target = before.Name,
                Detail = $"size {before.Size?.Width}x{before.Size?.Height} -> {after.Size?.Width}x{after.Size?.Height}"
            });
        }

        var beforeNodes = before.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var afterNodes = after.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        foreach (var id in beforeNodes.Keys)
            if (!afterNodes.ContainsKey(id))
                diff.Changes.Add(new DiffEntry { Kind = "node.removed", Target = id, Before = beforeNodes[id].Text });

        foreach (var (id, afterNode) in afterNodes)
        {
            if (!beforeNodes.TryGetValue(id, out var beforeNode))
            {
                diff.Changes.Add(new DiffEntry { Kind = "node.added", Target = id, After = afterNode.Text });
                continue;
            }

            void Compare(string property, string? b, string? a)
            {
                if (string.Equals(b, a, StringComparison.Ordinal)) return;
                diff.Changes.Add(new DiffEntry { Kind = "node.modified", Target = id, Property = property, Before = b, After = a });
            }

            Compare("text", beforeNode.Text, afterNode.Text);
            Compare("shape", beforeNode.Shape, afterNode.Shape);
            Compare("group", beforeNode.GroupId, afterNode.GroupId);
            Compare("fill", beforeNode.Style?.Fill, afterNode.Style?.Fill);
            Compare("stroke", beforeNode.Style?.Stroke, afterNode.Style?.Stroke);

            if (Math.Abs(beforeNode.Bounds.X - afterNode.Bounds.X) > 1e-6 ||
                Math.Abs(beforeNode.Bounds.Y - afterNode.Bounds.Y) > 1e-6 ||
                Math.Abs(beforeNode.Bounds.W - afterNode.Bounds.W) > 1e-6 ||
                Math.Abs(beforeNode.Bounds.H - afterNode.Bounds.H) > 1e-6)
            {
                diff.Changes.Add(new DiffEntry
                {
                    Kind = "node.modified",
                    Target = id,
                    Property = "bounds",
                    Before = $"({beforeNode.Bounds.X:F3},{beforeNode.Bounds.Y:F3}) {beforeNode.Bounds.W:F3}x{beforeNode.Bounds.H:F3}",
                    After = $"({afterNode.Bounds.X:F3},{afterNode.Bounds.Y:F3}) {afterNode.Bounds.W:F3}x{afterNode.Bounds.H:F3}"
                });
            }

            var beforeData = beforeNode.ShapeData ?? new Dictionary<string, string>();
            var afterData = afterNode.ShapeData ?? new Dictionary<string, string>();
            foreach (var key in beforeData.Keys.Union(afterData.Keys, StringComparer.Ordinal))
            {
                beforeData.TryGetValue(key, out var bv);
                afterData.TryGetValue(key, out var av);
                if (!string.Equals(bv, av, StringComparison.Ordinal))
                    diff.Changes.Add(new DiffEntry { Kind = "node.modified", Target = id, Property = $"shapeData.{key}", Before = bv, After = av });
            }
        }

        var beforeEdges = before.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var afterEdges = after.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal);

        foreach (var id in beforeEdges.Keys)
            if (!afterEdges.ContainsKey(id))
                diff.Changes.Add(new DiffEntry { Kind = "edge.removed", Target = id });

        foreach (var (id, afterEdge) in afterEdges)
        {
            if (!beforeEdges.TryGetValue(id, out var beforeEdge2))
            {
                diff.Changes.Add(new DiffEntry { Kind = "edge.added", Target = id, After = $"{afterEdge.From}->{afterEdge.To}" });
                continue;
            }
            if (!string.Equals(beforeEdge2.From, afterEdge.From, StringComparison.Ordinal) ||
                !string.Equals(beforeEdge2.To, afterEdge.To, StringComparison.Ordinal))
            {
                diff.Changes.Add(new DiffEntry
                {
                    Kind = "edge.modified",
                    Target = id,
                    Property = "endpoints",
                    Before = $"{beforeEdge2.From}->{beforeEdge2.To}",
                    After = $"{afterEdge.From}->{afterEdge.To}"
                });
            }
            if (!string.Equals(beforeEdge2.Label, afterEdge.Label, StringComparison.Ordinal))
                diff.Changes.Add(new DiffEntry { Kind = "edge.modified", Target = id, Property = "label", Before = beforeEdge2.Label, After = afterEdge.Label });
        }

        var beforeGroups = before.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var afterGroups = after.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);

        foreach (var id in beforeGroups.Keys)
            if (!afterGroups.ContainsKey(id))
                diff.Changes.Add(new DiffEntry { Kind = "group.removed", Target = id, Before = beforeGroups[id].Name });

        foreach (var (id, afterGroup) in afterGroups)
        {
            if (!beforeGroups.TryGetValue(id, out var beforeGroup))
            {
                diff.Changes.Add(new DiffEntry { Kind = "group.added", Target = id, After = afterGroup.Name });
                continue;
            }
            if (!string.Equals(beforeGroup.Name, afterGroup.Name, StringComparison.Ordinal))
                diff.Changes.Add(new DiffEntry { Kind = "group.modified", Target = id, Property = "name", Before = beforeGroup.Name, After = afterGroup.Name });

            var added = afterGroup.Members.Except(beforeGroup.Members, StringComparer.Ordinal).ToList();
            var removed = beforeGroup.Members.Except(afterGroup.Members, StringComparer.Ordinal).ToList();
            if (added.Count > 0 || removed.Count > 0)
            {
                diff.Changes.Add(new DiffEntry
                {
                    Kind = "group.modified",
                    Target = id,
                    Property = "members",
                    Before = removed.Count > 0 ? "-" + string.Join(",", removed) : null,
                    After = added.Count > 0 ? "+" + string.Join(",", added) : null
                });
            }
        }
    }
}

public sealed class DiffEntry
{
    public string Kind { get; init; } = "";
    public string Target { get; init; } = "";
    public string? Property { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
    public string? Detail { get; init; }

    public override string ToString() =>
        Property is null
            ? $"{Kind,-16} {Target}" + (Detail is null ? "" : $"  ({Detail})")
            : $"{Kind,-16} {Target}.{Property}: {Before} -> {After}";
}
