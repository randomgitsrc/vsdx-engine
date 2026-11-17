using System.IO.Compression;
using System.Xml.Linq;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Restores the original <c>Master</c> attribute on shapes that came from an external file.
///
/// Phase 8.1 limitation, now closed: the incremental editor rebuilds shapes from the model and
/// only re-creates masters it recognises. For an unrecognised master the outline is materialised
/// as geometry, so the shape still *looks* correct — but the shape silently stops being an
/// instance of that master, which means editing the master in Visio no longer updates it.
///
/// The accepted decision is explicit that the original file must never be damaged silently, so
/// this either restores the reference or records that it could not.
/// </summary>
public static class VisioMasterBridge
{
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";

    /// <summary>
    /// Re-applies <see cref="Node.SourceMaster"/> to each shape that declares one.
    /// Returns how many were restored.
    /// </summary>
    public static int Apply(Diagram diagram, string vsdxPath, CompatibilityReport? compat = null)
    {
        var wanted = new Dictionary<(int page, string visioId), string>();

        for (int pageIndex = 0; pageIndex < diagram.Pages.Count; pageIndex++)
        {
            foreach (var node in diagram.Pages[pageIndex].Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.SourceMaster)) continue;
                if (string.IsNullOrWhiteSpace(node.VisioShapeId)) continue;
                wanted[(pageIndex, node.VisioShapeId!)] = node.SourceMaster!;
            }
        }

        if (wanted.Count == 0) return 0;

        int restored = 0;
        int missing = 0;

        foreach (var pageIndex in wanted.Keys.Select(k => k.page).Distinct())
        {
            var entryName = $"visio/pages/page{pageIndex + 1}.xml";

            using var zip = ZipFile.Open(vsdxPath, ZipArchiveMode.Update);
            var entry = zip.GetEntry(entryName);
            if (entry is null) continue;

            XDocument doc;
            using (var stream = entry.Open()) doc = XDocument.Load(stream);

            var shapes = doc.Descendants(VisioNs + "Shape")
                .Where(s => (string?)s.Attribute("ID") is not null)
                .ToDictionary(s => (string)s.Attribute("ID")!, s => s, StringComparer.Ordinal);

            bool changed = false;
            int pageRestored = 0;

            foreach (var ((page, visioId), masterRef) in wanted.Where(w => w.Key.page == pageIndex))
            {
                if (!shapes.TryGetValue(visioId, out var shape))
                {
                    missing++;
                    continue;
                }

                // Do not overwrite a master the editor already resolved correctly.
                var existing = (string?)shape.Attribute("Master");
                if (string.Equals(existing, masterRef, StringComparison.Ordinal)) continue;

                shape.SetAttributeValue("Master", masterRef);
                pageRestored++;
                changed = true;
            }

            if (!changed) continue;

            entry.Delete();
            var replacement = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open(), new System.Text.UTF8Encoding(false));
            doc.Save(writer);

            restored += pageRestored;
        }

        if (restored > 0)
        {
            compat?.Info("write.master-restored",
                $"恢复了 {restored} 个形状的原始母版引用，避免静默丢失母版实例关系");
        }

        if (missing > 0)
        {
            // Explicit, not silent: the caller must be able to see that a link was not preserved.
            compat?.Warn("write.master-unresolved",
                $"{missing} 个形状在输出包中找不到对应形状，母版引用未能恢复");
        }

        return restored;
    }
}
