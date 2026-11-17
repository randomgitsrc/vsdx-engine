using System.IO.Compression;
using System.Xml.Linq;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Preserves the "shape has no explicit geometry" state across a save.
///
/// Phase 8 audit (P0): a shape whose geometry comes from its master — or that carries no
/// Geometry section at all — came back with a synthesised 5-row rectangle after a save. For a
/// rectangle that is harmless; for a circle, a diamond, a custom path or any master-backed
/// shape it overwrites the real outline.
///
/// Root cause, isolated by experiment (see docs/phase8/p0-geometry-injection.md):
/// OfficeIMO.Visio synthesises a rectangle whenever a shape has neither a Geometry section nor
/// a master reference. A master-backed shape round-trips intact (25-row ellipse stays 25 rows).
/// So the fix must be narrow: remember which shapes had no geometry on load, and remove only
/// the sections the library added for those, and only if they are the synthesised rectangle.
/// </summary>
public static class VisioGeometryPreserver
{
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";

    /// <summary>A synthesised rectangle is exactly these five rows; anything else is real geometry.</summary>
    private static readonly string[] RectangleRows =
        { "MoveTo", "LineTo", "LineTo", "LineTo", "LineTo" };

    /// <summary>
    /// Records, per page, the ids of shapes that carry no Geometry section.
    /// Call this on the *original* package before loading, then feed the result to
    /// <see cref="RestoreAfterSave"/> once the new package has been written.
    /// </summary>
    public static IReadOnlyDictionary<int, HashSet<string>> SnapshotUngeometriedShapes(string vsdxPath)
    {
        var result = new Dictionary<int, HashSet<string>>();

        using var zip = ZipFile.OpenRead(vsdxPath);
        foreach (var entry in zip.Entries.Where(e =>
                     e.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)
                     && e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            // page1.xml -> page id 0
            var name = Path.GetFileNameWithoutExtension(entry.FullName);
            if (!name.StartsWith("page", StringComparison.Ordinal)) continue;
            if (!int.TryParse(name[4..], out int oneBased)) continue;

            XDocument doc;
            using (var stream = entry.Open()) doc = XDocument.Load(stream);

            var ids = doc.Descendants(VisioNs + "Shape")
                .Where(s => !s.Elements(VisioNs + "Section")
                    .Any(sec => (string?)sec.Attribute("N") == "Geometry"))
                .Select(s => (string?)s.Attribute("ID"))
                .Where(id => id is not null)
                .Select(id => id!)
                .ToHashSet(StringComparer.Ordinal);

            if (ids.Count > 0) result[oneBased - 1] = ids;
        }

        return result;
    }

    /// <summary>
    /// Removes geometry the library synthesised for shapes that had none on load.
    /// Returns how many sections were removed. Shapes that legitimately gained geometry are left
    /// alone: only a section that is exactly the default rectangle is treated as synthesised.
    /// </summary>
    public static int RestoreAfterSave(
        string vsdxPath,
        IReadOnlyDictionary<int, HashSet<string>> ungeometried,
        CompatibilityReport? compat = null)
    {
        if (ungeometried.Count == 0) return 0;

        int removed = 0;
        using var zip = ZipFile.Open(vsdxPath, ZipArchiveMode.Update);

        foreach (var (pageId, shapeIds) in ungeometried)
        {
            var entryName = $"visio/pages/page{pageId + 1}.xml";
            var entry = zip.GetEntry(entryName);
            if (entry is null) continue;

            XDocument doc;
            using (var stream = entry.Open()) doc = XDocument.Load(stream);

            bool changed = false;
            foreach (var shape in doc.Descendants(VisioNs + "Shape"))
            {
                var id = (string?)shape.Attribute("ID");
                if (id is null || !shapeIds.Contains(id)) continue;

                // A shape that now has a master legitimately keeps its master, not a geometry.
                if (shape.Attribute("Master") is not null || shape.Attribute("MasterShape") is not null)
                    continue;

                var geometry = shape.Elements(VisioNs + "Section")
                    .Where(sec => (string?)sec.Attribute("N") == "Geometry")
                    .ToList();

                foreach (var section in geometry)
                {
                    if (!IsSynthesisedRectangle(section)) continue;
                    section.Remove();
                    removed++;
                    changed = true;
                }
            }

            if (!changed) continue;

            entry.Delete();
            var replacement = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open(), new System.Text.UTF8Encoding(false));
            doc.Save(writer);
        }

        if (removed > 0)
        {
            compat?.Info("write.geometry-preserved",
                $"移除了 {removed} 个由后端合成的默认矩形几何段，保持原形状不变");
        }

        return removed;
    }

    /// <summary>
    /// True when the section is exactly the library's default rectangle: one MoveTo plus four
    /// LineTo rows over the shape's own width/height. Real geometry (an ellipse has ~25 rows, a
    /// custom path has arbitrary rows) never matches.
    /// </summary>
    private static bool IsSynthesisedRectangle(XElement section)
    {
        var rows = section.Elements(VisioNs + "Row").ToList();
        if (rows.Count != RectangleRows.Length) return false;

        for (int i = 0; i < rows.Count; i++)
            if ((string?)rows[i].Attribute("T") != RectangleRows[i]) return false;

        return true;
    }
}
