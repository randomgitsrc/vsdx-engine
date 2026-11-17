using System.IO.Compression;
using System.Xml.Linq;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Restores the page's shape z-order after a save.
///
/// Phase 8 audit (P1): <c>Page.ShapeOrder</c> was populated on read but never consumed on write,
/// so every save reordered the page to our writer's convenience (containers, then nodes, then
/// connectors) and the original stacking was lost. In Visio z-order *is* document order, so this
/// showed up as objects jumping in front of or behind each other after an edit.
///
/// The fix is a post-save reorder of the &lt;Shape&gt; elements in the page part rather than a
/// rewrite of the writer's creation order, which has real dependencies (containers must exist
/// before members reference them). Reordering at the end satisfies both.
/// </summary>
public static class VisioZOrderBridge
{
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";

    /// <summary>
    /// Reorders shape elements so their sequence matches <paramref name="shapeOrder"/>.
    /// Shapes not named in the list keep their relative order and are appended after the named
    /// ones, which is the safe default when the model only covers part of the page.
    /// </summary>
    public static int Apply(Diagram diagram, string vsdxPath, CompatibilityReport? compat = null)
    {
        int reordered = 0;

        for (int pageIndex = 0; pageIndex < diagram.Pages.Count; pageIndex++)
        {
            var pageModel = diagram.Pages[pageIndex];
            if (pageModel.ShapeOrder is not { Count: > 0 }) continue;

            reordered += ApplyToPage(vsdxPath, pageIndex, pageModel, compat);
        }

        return reordered;
    }

    private static int ApplyToPage(
        string vsdxPath, int pageIndex, Page pageModel, CompatibilityReport? compat)
    {
        var entryName = $"visio/pages/page{pageIndex + 1}.xml";

        using var zip = ZipFile.Open(vsdxPath, ZipArchiveMode.Update);
        var entry = zip.GetEntry(entryName);
        if (entry is null) return 0;

        XDocument doc;
        using (var stream = entry.Open()) doc = XDocument.Load(stream);

        var shapesContainer = doc.Descendants(VisioNs + "Shapes").FirstOrDefault();
        if (shapesContainer is null) return 0;

        var shapes = shapesContainer.Elements(VisioNs + "Shape").ToList();
        if (shapes.Count <= 1) return 0;

        // Map every shape to the model id it represents. The writer stores the model id either as
        // the shape's Name (for nodes) or as the shape id itself, so match on both.
        var byModelId = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var shape in shapes)
        {
            var id = (string?)shape.Attribute("ID");
            var name = (string?)shape.Attribute("Name");
            if (id is not null) byModelId[id] = shape;
            if (!string.IsNullOrEmpty(name) && !byModelId.ContainsKey(name!)) byModelId[name!] = shape;
        }

        // Build the desired sequence: named shapes first in listed order, then the rest.
        var ordered = new List<XElement>();
        var taken = new HashSet<XElement>();

        foreach (var modelId in pageModel.ShapeOrder!)
        {
            if (!byModelId.TryGetValue(modelId, out var shape)) continue;
            if (!taken.Add(shape)) continue;
            ordered.Add(shape);
        }

        int named = ordered.Count;
        foreach (var shape in shapes)
            if (taken.Add(shape)) ordered.Add(shape);

        if (named == 0)
        {
            compat?.Warn("write.shape-order-unmatched",
                $"页面 '{pageModel.Name}' 的 ShapeOrder 未匹配到任何形状，已忽略", pageModel.Name);
            return 0;
        }

        // Already in order?
        bool differs = false;
        for (int i = 0; i < shapes.Count; i++)
        {
            if (ReferenceEquals(shapes[i], ordered[i])) continue;
            differs = true;
            break;
        }
        if (!differs) return 0;

        // Reattach in the new sequence: removing then re-adding appends, so the order of the
        // removals does not matter.
        foreach (var shape in shapes) shape.Remove();
        foreach (var shape in ordered) shapesContainer.Add(shape);

        entry.Delete();
        var replacement = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(replacement.Open(), new System.Text.UTF8Encoding(false));
        doc.Save(writer);

        compat?.Info("write.shape-order-applied",
            $"已按 ShapeOrder 恢复 {named} 个对象的叠放次序", pageModel.Name);

        return named;
    }

    /// <summary>
    /// Reads the current shape order from a package, for verification.
    /// Returns the shape ids in document order (bottom to top in Visio terms).
    /// </summary>
    public static IReadOnlyList<string> ReadOrder(string vsdxPath, int pageIndex = 0)
    {
        using var zip = ZipFile.OpenRead(vsdxPath);
        var entry = zip.GetEntry($"visio/pages/page{pageIndex + 1}.xml");
        if (entry is null) return Array.Empty<string>();

        XDocument doc;
        using (var stream = entry.Open()) doc = XDocument.Load(stream);

        return doc.Descendants(VisioNs + "Shape")
            .Select(s => (string?)s.Attribute("ID"))
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
    }
}
