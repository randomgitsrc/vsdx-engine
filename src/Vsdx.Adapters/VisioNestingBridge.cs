using System.IO.Compression;
using System.Xml.Linq;
using OfficeIMO.Visio;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Restores container-in-container nesting by patching the page XML after the package is written.
///
/// OfficeIMO.Visio has no public API for shape parenting: `VisioShape.Parent` has an internal
/// setter and `VisioPage` exposes no nest method. Auditing this (Phase 7, G1) showed the previous
/// behaviour degraded nesting into sibling containers and lost the parent link entirely, so the
/// hierarchy could not survive a save/read cycle.
///
/// The patch is deliberately narrow: it adds a <c>Parent</c> cell to the child container shape so
/// the child's coordinate space hangs off its parent, which is how Visio models nesting. Nothing
/// else in the part is touched.
/// </summary>
public static class VisioNestingBridge
{
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";

    /// <summary>Nests each child container under its parent after the package has been saved.</summary>
    public static void Apply(Diagram diagram, string vsdxPath, CompatibilityReport? compat = null)
    {
        foreach (var page in diagram.Pages)
        {
            var nested = page.Groups
                .Where(g => !string.IsNullOrWhiteSpace(g.ParentId))
                .ToList();

            if (nested.Count == 0) continue;

            // Resolve parent/child to the Visio shape ids actually present in the package.
            var byGroupId = page.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
            // The writer assigns container shapes sequential package IDs and stores the group
            // name as the container heading, so the heading is the reliable join key.
            var pairs = new List<(string childKey, string parentKey)>();

            foreach (var group in nested)
            {
                if (!byGroupId.TryGetValue(group.ParentId!, out var parent)) continue;

                var childKey = group.Name ?? group.Id;
                var parentKey = parent.Name ?? parent.Id;
                if (string.Equals(childKey, parentKey, StringComparison.Ordinal)) continue;

                pairs.Add((childKey, parentKey));
            }

            if (pairs.Count == 0) continue;

            try
            {
                NestInPackage(vsdxPath, page.VisioId ?? 0, pairs, page, compat);
            }
            catch (Exception ex)
            {
                compat?.Warn("write.nested-group-failed",
                    $"嵌套容器结构未写入：{ex.Message}", page.Name);
            }
        }
    }

    private static void NestInPackage(
        string vsdxPath, int pageIndex, List<(string childKey, string parentKey)> pairs,
        Page page, CompatibilityReport? compat)
    {
        using var zip = ZipFile.Open(vsdxPath, ZipArchiveMode.Update);

        // Page parts are 1-based in the package: page1.xml corresponds to page id 0.
        var entryName = $"visio/pages/page{pageIndex + 1}.xml";
        var entry = zip.GetEntry(entryName);
        if (entry is null)
        {
            compat?.Warn("write.nested-group-missing-part",
                $"找不到页面部件 {entryName}，嵌套结构未写入", page.Name);
            return;
        }

        XDocument doc;
        using (var stream = entry.Open()) doc = XDocument.Load(stream);

        // Containers carry the group name as their heading text; that is the join key.
        var containersByText = doc.Descendants(VisioNs + "Shape")
            .Where(s => (string?)s.Attribute("NameU") is "Container" or "Group")
            .Select(s => (shape: s, text: ((string?)s.Element(VisioNs + "Text"))?.Trim() ?? ""))
            .Where(t => !string.IsNullOrEmpty(t.text))
            .GroupBy(t => t.text, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().shape, StringComparer.Ordinal);

        int applied = 0;
        foreach (var (childKey, parentKey) in pairs)
        {
            if (!containersByText.TryGetValue(childKey, out var child) ||
                !containersByText.TryGetValue(parentKey, out var parent))
            {
                compat?.Warn("write.nested-group-unresolved",
                    $"嵌套容器 '{childKey}' → '{parentKey}' 在包内无法定位", childKey);
                continue;
            }

            var childNumericId = (string)child.Attribute("ID")!;
            if (!int.TryParse(childNumericId, out _))
            {
                compat?.Warn("write.nested-group-nonnumeric",
                    $"容器 '{childKey}' 的 Shape ID 非数字，无法建立父子关系", childKey);
                continue;
            }

            // Visio expresses nesting with a Parent cell naming the parent shape id.
            var cells = child.Elements(VisioNs + "Cell").ToList();

            var parentCell = cells.FirstOrDefault(c => (string?)c.Attribute("N") == "Parent");
            if (parentCell is not null)
            {
                parentCell.SetAttributeValue("V", (string)parent.Attribute("ID")!);
            }
            else
            {
                // Parent must precede geometry cells for Visio to honour it.
                var anchor = cells.FirstOrDefault(c => (string?)c.Attribute("N") is "PinX" or "Width");
                var created = new XElement(VisioNs + "Cell",
                    new XAttribute("N", "Parent"),
                    new XAttribute("V", (string)parent.Attribute("ID")!));

                if (anchor is not null) anchor.AddBeforeSelf(created);
                else child.AddFirst(created);
            }

            applied++;
        }

        if (applied > 0)
        {
            entry.Delete();
            var replacement = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open(), new System.Text.UTF8Encoding(false));
            doc.Save(writer);

            compat?.Info("write.nested-group-applied",
                $"已建立 {applied} 组嵌套容器父子关系", page.Name);
        }
    }

}
