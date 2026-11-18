using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using OfficeIMO.Visio;
using Vsdx.Model;

namespace Vsdx.Validator;

/// <summary>
/// Validates a VSDX package at four independent levels:
/// ZIP integrity, OOXML/OPC structure, Visio content semantics, and (optionally) reload.
/// Each check is reported individually so a passing run is evidence, not a single boolean.
/// </summary>
public sealed class VsdxPackageValidatorService : IDocumentValidator
{
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace CtNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    public ValidationResult Validate(string vsdxPath, ValidationOptions? options = null)
    {
        options ??= new ValidationOptions();
        var result = new ValidationResult();

        if (!File.Exists(vsdxPath))
        {
            result.Error("file.missing", $"File not found: {vsdxPath}");
            return result;
        }

        // 1. ZIP integrity
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(vsdxPath);
        }
        catch (Exception ex)
        {
            result.Error("zip.unreadable", $"Not a readable ZIP/OPC package: {ex.Message}");
            return result;
        }

        using (archive)
        {
            var names = archive.Entries.Select(e => e.FullName).ToList();
            result.Check("zip.readable", true, $"{names.Count} entries");
            result.Check("zip.has-content-types", names.Contains("[Content_Types].xml"),
                names.Contains("[Content_Types].xml") ? null : "missing [Content_Types].xml");

            if (!names.Contains("[Content_Types].xml"))
                result.Error("opc.content-types-missing", "Package is missing [Content_Types].xml.");

            if (!names.Contains("_rels/.rels"))
                result.Error("opc.root-rels-missing", "Package is missing _rels/.rels.");

            // 2. XML parseability of every XML part
            int xmlParts = 0, badXml = 0;
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                                                          || e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            {
                xmlParts++;
                try
                {
                    using var stream = entry.Open();
                    using var reader = XmlReader.Create(stream, new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        MaxCharactersInDocument = 50_000_000
                    });
                    XDocument.Load(reader);
                }
                catch (Exception ex)
                {
                    badXml++;
                    result.Error("xml.unparseable", $"Part '{entry.FullName}' is not well-formed XML: {ex.Message}", entry.FullName);
                }
            }
            result.Check("xml.parseable", badXml == 0, $"{xmlParts} XML parts checked, {badXml} invalid");

            // 3. OPC content types and relationships
            ValidateContentTypes(archive, result);
            ValidateRelationships(archive, result);

            // 4. Visio semantics
            var pageShapeIds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            ValidateVisioStructure(archive, result, pageShapeIds);
            ValidateShapeIdUniqueness(archive, result, pageShapeIds);
            ValidateConnectorReferences(archive, result);
            ValidateGroupMembership(archive, result);
            ValidateMasterReferences(archive, result);
        }

        // 5. Reload through the backend
        if (options.Deep)
        {
            try
            {
                var doc = VisioDocument.Load(vsdxPath);
                int shapeCount = doc.Pages.Sum(p => p.Shapes.Count);
                int connectorCount = doc.Pages.Sum(p => p.Connectors.Count);
                result.Check("backend.reload", true,
                    $"{doc.Pages.Count} page(s), {shapeCount} shape(s), {connectorCount} connector(s)");
                if (doc.Pages.Count == 0)
                    result.Error("visio.no-pages", "Document reloaded with zero pages.");
            }
            catch (Exception ex)
            {
                result.Check("backend.reload", false, ex.Message);
                result.Error("backend.reload-failed", $"OfficeIMO.Visio could not reload the package: {ex.Message}");
            }
        }

        // 6. Optional render smoke test
        if (options.RenderSmoke)
        {
            try
            {
                var doc = VisioDocument.Load(vsdxPath);
                var svg = doc.Pages[0].ToSvg(new VisioSvgSaveOptions { PageIndex = 0 });
                bool ok = !string.IsNullOrWhiteSpace(svg) && svg.Contains("<svg", StringComparison.OrdinalIgnoreCase);
                result.Check("render.svg-smoke", ok, ok ? $"{svg.Length} chars" : "empty SVG");
                if (!ok) result.Error("render.svg-empty", "SVG render produced no output.");
            }
            catch (Exception ex)
            {
                result.Check("render.svg-smoke", false, ex.Message);
                result.Error("render.failed", $"Render smoke test failed: {ex.Message}");
            }
        }

        return result;
    }

    private static void ValidateContentTypes(ZipArchive archive, ValidationResult result)
    {
        var entry = archive.GetEntry("[Content_Types].xml");
        if (entry is null) return;

        XDocument doc;
        try
        {
            using var s = entry.Open();
            doc = XDocument.Load(s);
        }
        catch { return; }

        var overrides = doc.Root?.Elements(CtNs + "Override").ToList() ?? new List<XElement>();
        var defaults = doc.Root?.Elements(CtNs + "Default").ToList() ?? new List<XElement>();

        bool HasOverride(string part, string type) =>
            overrides.Any(o => string.Equals((string?)o.Attribute("PartName"), part, StringComparison.Ordinal)
                            && string.Equals((string?)o.Attribute("ContentType"), type, StringComparison.Ordinal));

        result.Check("opc.document-override", HasOverride("/visio/document.xml", "application/vnd.ms-visio.drawing.main+xml"),
            "visio/document.xml must be the drawing main part");
        if (!HasOverride("/visio/document.xml", "application/vnd.ms-visio.drawing.main+xml"))
            result.Error("opc.document-override", "Missing Override for /visio/document.xml -> application/vnd.ms-visio.drawing.main+xml.");

        result.Check("opc.pages-override", HasOverride("/visio/pages/pages.xml", "application/vnd.ms-visio.pages+xml"));
        if (!HasOverride("/visio/pages/pages.xml", "application/vnd.ms-visio.pages+xml"))
            result.Error("opc.pages-override", "Missing Override for /visio/pages/pages.xml -> application/vnd.ms-visio.pages+xml.");

        bool relsDefault = defaults.Any(d => string.Equals((string?)d.Attribute("Extension"), "rels", StringComparison.Ordinal)
            && ((string?)d.Attribute("ContentType"))?.Contains("relationships+xml") == true);
        result.Check("opc.rels-default", relsDefault);
        if (!relsDefault) result.Error("opc.rels-default", "Missing Default content type for the .rels extension.");

        // Every page part must have a page override.
        var pageParts = archive.Entries
            .Where(e => e.FullName.StartsWith("visio/pages/", StringComparison.OrdinalIgnoreCase)
                     && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                     && !e.FullName.EndsWith("pages.xml", StringComparison.OrdinalIgnoreCase)
                     && !e.FullName.Contains("_rels"))
            .Select(e => "/" + e.FullName)
            .ToList();

        foreach (var part in pageParts)
        {
            bool ok = HasOverride(part, "application/vnd.ms-visio.page+xml");
            if (!ok) result.Error("opc.page-override", $"Missing Override for {part} -> application/vnd.ms-visio.page+xml.", part);
        }
        result.Check("opc.page-overrides", pageParts.All(p => HasOverride(p, "application/vnd.ms-visio.page+xml")),
            $"{pageParts.Count} page part(s)");

        // A part must never be declared with two different content types.
        var duplicates = overrides
            .GroupBy(o => (string?)o.Attribute("PartName"))
            .Where(g => g.Key is not null && g.Select(x => (string?)x.Attribute("ContentType")).Distinct().Count() > 1)
            .Select(g => g.Key!)
            .ToList();
        foreach (var dup in duplicates)
            result.Error("opc.duplicate-override", $"Part '{dup}' is declared with more than one content type.", dup);
        result.Check("opc.no-duplicate-overrides", duplicates.Count == 0);
    }

    private static void ValidateRelationships(ZipArchive archive, ValidationResult result)
    {
        var rootRels = archive.GetEntry("_rels/.rels");
        if (rootRels is null)
        {
            result.Error("opc.root-rels", "Package is missing _rels/.rels.");
            return;
        }

        var doc = LoadXml(rootRels);
        if (doc is null)
        {
            result.Error("opc.root-rels-invalid", "_rels/.rels is not valid XML.");
            return;
        }

        var relationships = doc.Root?.Elements(RelNs + "Relationship").ToList() ?? new List<XElement>();
        result.Check("opc.root-rels-count", relationships.Count > 0, $"{relationships.Count} relationship(s)");

        bool documentTargeted = relationships.Any(r =>
        {
            var target = ((string?)r.Attribute("Target"))?.TrimStart('/');
            var type = (string?)r.Attribute("Type");
            return string.Equals(target, "visio/document.xml", StringComparison.OrdinalIgnoreCase)
                && type is not null && type.Contains("visio", StringComparison.OrdinalIgnoreCase);
        });
        result.Check("opc.document-relationship", documentTargeted);
        if (!documentTargeted)
            result.Error("opc.document-relationship", "Root relationships must target visio/document.xml with the Visio document relationship type.");

        // document.xml.rels must point at pages/pages.xml
        var docRels = archive.GetEntry("visio/_rels/document.xml.rels");
        if (docRels is null)
        {
            result.Error("opc.document-rels", "Missing visio/_rels/document.xml.rels.");
        }
        else
        {
            var docRelXml = LoadXml(docRels);
            bool pagesLinked = docRelXml?.Root?.Elements(RelNs + "Relationship")
                .Any(r => ((string?)r.Attribute("Target"))?.EndsWith("pages/pages.xml", StringComparison.OrdinalIgnoreCase) == true
                       && ((string?)r.Attribute("Type"))?.Contains("visio", StringComparison.OrdinalIgnoreCase) == true) ?? false;
            result.Check("opc.pages-relationship", pagesLinked);
            if (!pagesLinked)
                result.Error("opc.pages-relationship", "visio/document.xml does not relate to pages/pages.xml with a Visio pages relationship.");
        }

        // Every relationship ID must be unique within its part.
        foreach (var relsEntry in archive.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var relsXml = LoadXml(relsEntry);
            if (relsXml is null) continue;
            var ids = relsXml.Root?.Elements(RelNs + "Relationship")
                .Select(r => (string?)r.Attribute("Id"))
                .Where(id => id is not null)
                .ToList() ?? new List<string?>();
            var dupes = ids.GroupBy(i => i!).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            foreach (var dup in dupes)
                result.Error("opc.duplicate-rel-id", $"Relationship id '{dup}' is duplicated in '{relsEntry.FullName}'.", relsEntry.FullName);
        }
    }

    private static void ValidateVisioStructure(
        ZipArchive archive, ValidationResult result, Dictionary<string, HashSet<string>> pageShapeIds)
    {
        var pagesXml = archive.GetEntry("visio/pages/pages.xml");
        if (pagesXml is null)
        {
            result.Error("visio.pages-missing", "Package is missing visio/pages/pages.xml.");
            return;
        }

        var doc = LoadXml(pagesXml);
        if (doc is null)
        {
            result.Error("visio.pages-invalid", "visio/pages/pages.xml is not valid XML.");
            return;
        }

        var pages = doc.Root?.Elements(VisioNs + "Page").ToList() ?? new List<XElement>();
        result.Check("visio.pages-present", pages.Count > 0, $"{pages.Count} page(s)");
        if (pages.Count == 0)
        {
            result.Error("visio.pages-present", "pages.xml contains no Page element.");
            return;
        }

        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var idAttr = (string?)page.Attribute("ID");
            if (!int.TryParse(idAttr, out int pageId))
                result.Error("visio.page-id", $"Page #{i} has a non-numeric ID '{idAttr}'.", idAttr);
            else if (pageId < 0)
                result.Error("visio.page-id", $"Page #{i} has a negative ID {pageId}.", idAttr);

            var name = (string?)page.Attribute("Name") ?? $"#{i}";
            var rel = page.Elements(VisioNs + "Rel").FirstOrDefault();
            if (rel is null)
            {
                result.Error("visio.page-rel", $"Page '{name}' has no <Rel> element pointing at its page part.", name);
                continue;
            }

            var relId = rel.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
            if (string.IsNullOrWhiteSpace(relId))
            {
                result.Error("visio.page-rel-id", $"Page '{name}' has an empty relationship reference.", name);
                continue;
            }

            var pageRelsXml = archive.GetEntry("visio/pages/_rels/pages.xml.rels");
            if (pageRelsXml is null)
            {
                result.Error("visio.pages-rels", "Missing visio/pages/_rels/pages.xml.rels.");
                break;
            }

            var relsDoc = LoadXml(pageRelsXml);
            var relationship = relsDoc?.Root?.Elements(RelNs + "Relationship")
                .FirstOrDefault(r => (string?)r.Attribute("Id") == relId);
            if (relationship is null)
            {
                result.Error("visio.page-rel-missing", $"Page '{name}' references relationship '{relId}' which is not declared.", name);
                continue;
            }

            var target = (string?)relationship.Attribute("Target");
            if (string.IsNullOrWhiteSpace(target))
            {
                result.Error("visio.page-rel-target", $"Relationship '{relId}' has no Target.", name);
                continue;
            }

            var partName = "visio/pages/" + target!.Replace('\\', '/').TrimStart('/').Replace("visio/pages/", "");
            var entry = archive.GetEntry(partName);
            if (entry is null)
            {
                result.Error("visio.page-part-missing", $"Page '{name}' targets missing part '{partName}'.", partName);
                continue;
            }

            var pageDoc = LoadXml(entry);
            if (pageDoc is null)
            {
                result.Error("visio.page-part-invalid", $"Page part '{partName}' is not valid XML.", partName);
                continue;
            }

            var shapeIds = new HashSet<string>(StringComparer.Ordinal);
            int shapes = 0;
            foreach (var shape in pageDoc.Descendants(VisioNs + "Shape"))
            {
                shapes++;
                var shapeId = (string?)shape.Attribute("ID");
                if (string.IsNullOrWhiteSpace(shapeId))
                {
                    result.Error("visio.shape-id", $"Page '{name}' has a shape without an ID.", name);
                    continue;
                }
                if (!int.TryParse(shapeId, out _))
                    result.Error("visio.shape-id", $"Page '{name}' has a non-numeric shape ID '{shapeId}'.", shapeId);
                if (!shapeIds.Add(shapeId!))
                    result.Error("visio.shape-id-duplicate", $"Page '{name}' has duplicate shape ID '{shapeId}'.", shapeId);
            }
            pageShapeIds[partName] = shapeIds;
            result.Check($"visio.page[{name}].shapes", shapes > 0, $"{shapes} shape(s), {shapeIds.Count} unique id(s)");
        }
    }

    private static void ValidateShapeIdUniqueness(
        ZipArchive archive, ValidationResult result, Dictionary<string, HashSet<string>> pageShapeIds)
    {
        if (pageShapeIds.Count == 0) return;
        bool allUnique = pageShapeIds.Values.All(ids => ids.Count > 0);
        result.Check("visio.shape-ids-unique", allUnique,
            string.Join(", ", pageShapeIds.Select(kv => $"{Path.GetFileName(kv.Key)}={kv.Value.Count}")));
    }

    /// <summary>
    /// Every `Master="n"` on a shape must name a master the package actually declares.
    ///
    /// Visio shows a dangling reference as a broken shape; the validator used to return VALID for it. The
    /// engine never writes one — but an externally edited file can carry one, and that is exactly the case
    /// validation exists for.
    /// </summary>
    private static void ValidateMasterReferences(ZipArchive archive, ValidationResult result)
    {
        var mastersEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName, "visio/masters/masters.xml", StringComparison.OrdinalIgnoreCase));

        // No masters part at all: nothing to resolve against, and nothing to complain about.
        if (mastersEntry is null) return;

        var declared = new HashSet<string>(StringComparer.Ordinal);

        // `LoadXml` returns null for a part that does not parse. That has already been reported as
        // `xml.unparseable` by the structural pass, so there is nothing to resolve here.
        var mastersDoc = LoadXml(mastersEntry);
        if (mastersDoc is null) return;

        foreach (var master in mastersDoc.Descendants()
                     .Where(e => e.Name.LocalName == "Master"))
        {
            var id = master.Attribute("ID")?.Value;

            if (!string.IsNullOrEmpty(id)) declared.Add(id);
        }

        var dangling = new List<string>();

        foreach (var entry in archive.Entries.Where(e =>
                     e.FullName.StartsWith("visio/pages/", StringComparison.OrdinalIgnoreCase)
                     && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                     && !e.FullName.Contains("_rels", StringComparison.Ordinal)
                     && !e.FullName.EndsWith("pages.xml", StringComparison.OrdinalIgnoreCase)))
        {
            var pageDoc = LoadXml(entry);
            if (pageDoc is null) continue;

            foreach (var shape in pageDoc.Descendants().Where(e => e.Name.LocalName == "Shape"))
            {
                var referenced = shape.Attribute("Master")?.Value;

                if (string.IsNullOrEmpty(referenced)) continue;

                // Inherited from a container's master, not a direct reference.
                if (string.Equals(referenced, "0", StringComparison.Ordinal)) continue;

                if (!declared.Contains(referenced))
                    dangling.Add($"{Path.GetFileName(entry.FullName)}#shape {shape.Attribute("ID")?.Value} -> master {referenced}");
            }
        }

        result.Check("visio.master-references",
            dangling.Count == 0,
            dangling.Count == 0
                ? $"{declared.Count} master(s) declared"
                : "dangling: " + string.Join("; ", dangling.Take(5)));

        // Also an error, not only a failed check: Visio draws a dangling reference as a broken shape, so a
        // package carrying one is not deliverable. `Check` alone would leave `IsValid` true — the file looked
        // valid precisely because nothing here raised an error.
        if (dangling.Count > 0)
        {
            result.Error(
                "visio.master-reference-missing",
                $"形状引用了不存在的 Master：{string.Join("; ", dangling.Take(5))}",
                "visio/masters/masters.xml");
        }
    }

    private static void ValidateConnectorReferences(ZipArchive archive, ValidationResult result)
    {
        int totalConnects = 0, badConnects = 0;
        foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("visio/pages/", StringComparison.OrdinalIgnoreCase)
                                                      && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                                                      && !e.FullName.Contains("_rels")
                                                      && !e.FullName.EndsWith("pages.xml", StringComparison.OrdinalIgnoreCase)))
        {
            var doc = LoadXml(entry);
            if (doc is null) continue;

            var shapeIds = doc.Descendants(VisioNs + "Shape")
                .Select(s => (string?)s.Attribute("ID"))
                .Where(id => id is not null)
                .Select(id => id!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var connect in doc.Descendants(VisioNs + "Connect"))
            {
                totalConnects++;
                var fromSheet = (string?)connect.Attribute("FromSheet");
                var toSheet = (string?)connect.Attribute("ToSheet");
                if (fromSheet is null || !shapeIds.Contains(fromSheet))
                {
                    badConnects++;
                    result.Error("visio.connect-from", $"Connect references missing FromSheet '{fromSheet}' in '{entry.FullName}'.", entry.FullName);
                }
                if (toSheet is null || !shapeIds.Contains(toSheet))
                {
                    badConnects++;
                    result.Error("visio.connect-to", $"Connect references missing ToSheet '{toSheet}' in '{entry.FullName}'.", entry.FullName);
                }
            }

            foreach (var shape in doc.Descendants(VisioNs + "Shape"))
            {
                var id = (string?)shape.Attribute("ID");
                var type = (string?)shape.Attribute("Type");
                if (!string.Equals(type, "Shape", StringComparison.Ordinal)) continue;
                if (shape.Descendants(VisioNs + "Connect").Any()) continue;
                var master = (string?)shape.Attribute("Master");
                var nameU = (string?)shape.Attribute("NameU");
                if (master is not null && nameU is null && id is not null)
                    result.Warn("visio.shape-master-no-name", $"Shape '{id}' has Master '{master}' but no NameU.", id);
            }
        }

        result.Check("visio.connector-references", badConnects == 0, $"{totalConnects} connect row(s), {badConnects} invalid");
    }

    private static void ValidateGroupMembership(ZipArchive archive, ValidationResult result)
    {
        int groups = 0, badRefs = 0;
        foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("visio/pages/", StringComparison.OrdinalIgnoreCase)
                                                      && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                                                      && !e.FullName.Contains("_rels")
                                                      && !e.FullName.EndsWith("pages.xml", StringComparison.OrdinalIgnoreCase)))
        {
            var doc = LoadXml(entry);
            if (doc is null) continue;

            var shapeIds = doc.Descendants(VisioNs + "Shape")
                .Select(s => (string?)s.Attribute("ID"))
                .Where(id => id is not null)
                .Select(id => id!)
                .ToHashSet(StringComparer.Ordinal);

            // Shape/@Parent must point to a real shape.
            foreach (var shape in doc.Descendants(VisioNs + "Shape"))
            {
                var parentId = (string?)shape.Descendants(VisioNs + "Cell").FirstOrDefault(c => (string?)c.Attribute("N") == "Parent")?.Attribute("V");
                if (parentId is null) continue;
                groups++;
                if (!shapeIds.Contains(parentId))
                {
                    badRefs++;
                    result.Error("visio.group-parent", $"Shape '{(string?)shape.Attribute("ID")}' references missing parent '{parentId}'.", (string?)shape.Attribute("ID"));
                }
            }
        }

        result.Check("visio.group-membership", badRefs == 0, $"{groups} parent link(s), {badRefs} broken");
    }

    private static XDocument? LoadXml(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 50_000_000
            });
            return XDocument.Load(reader);
        }
        catch
        {
            return null;
        }
    }
}
