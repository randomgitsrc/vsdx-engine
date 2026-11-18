using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Carries JSON model metadata (change history, timestamps, generator) through the VSDX
/// package so a later load can reconstruct it.
///
/// The metadata is stored as an opaque custom document property — a string containing the
/// serialised model metadata. It is additive: OfficeIMO does not expose custom properties,
/// so this patches <c>docProps/custom.xml</c> after the package is written. Nothing else in
/// the package is touched, so this cannot affect shape geometry or Visio compatibility.
/// </summary>
public static class VisioMetadataBridge
{
    private const string PropertyName = "AgentVsdxModelMetadata";
    private const string CustomPropsPart = "docProps/custom.xml";
    private const string CustomPropsContentType =
        "application/vnd.openxmlformats-officedocument.custom-properties+xml";
    private static readonly XNamespace CustNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    private static readonly XNamespace CtNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string CustomRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Writes the model metadata into the package at <paramref name="vsdxPath"/>.</summary>
    public static void Write(Diagram diagram, string vsdxPath)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new PersistedMetadata
            {
                ModelVersion = diagram.ModelVersion,
                Created = diagram.Metadata.Created,
                Modified = diagram.Metadata.Modified,
                SourceFile = diagram.Metadata.SourceFile,
                Description = diagram.Metadata.Description,
                Source = diagram.Metadata.Source,
                Tags = diagram.Metadata.Tags,
                Generator = diagram.Metadata.Generator,
                Backend = diagram.Metadata.Backend,
                BackendVersion = diagram.Metadata.BackendVersion,
                History = diagram.Metadata.History
                    .Select(h => new PersistedChange
                    {
                        At = h.At, Action = h.Action, Target = h.Target, Detail = h.Detail, Tool = h.Tool
                    })
                    .ToList()
            }, JsonOptions);

            using var zip = ZipFile.Open(vsdxPath, ZipArchiveMode.Update);
            WriteCustomProperties(zip, payload);
            EnsureRelationshipAndContentType(zip);
        }
        catch (Exception)
        {
            // Metadata is an enhancement: a failure here must never fail the document write.
        }
    }

    /// <summary>Reads metadata previously written by <see cref="Write"/>, if present.</summary>
    public static Metadata? Read(string vsdxPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(vsdxPath);
            var entry = zip.GetEntry(CustomPropsPart);
            if (entry is null) return null;

            XDocument doc;
            using (var stream = entry.Open()) doc = XDocument.Load(stream);

            var property = doc.Root?.Elements(CustNs + "property")
                .FirstOrDefault(e => string.Equals((string?)e.Attribute("name"), PropertyName, StringComparison.Ordinal));
            var raw = property?.Value;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var persisted = JsonSerializer.Deserialize<PersistedMetadata>(raw, JsonOptions);
            if (persisted is null) return null;

            var metadata = new Metadata
            {
                ModelVersion = persisted.ModelVersion ?? ModelVersion.Current,
                Created = persisted.Created,
                Modified = persisted.Modified,
                SourceFile = persisted.SourceFile,
                Description = persisted.Description,
                Source = persisted.Source,
                Tags = persisted.Tags,
                Generator = persisted.Generator,
                Backend = persisted.Backend,
                BackendVersion = persisted.BackendVersion
            };
            foreach (var change in persisted.History)
            {
                metadata.History.Add(new ChangeRecord
                {
                    At = change.At,
                    Action = change.Action ?? "modify",
                    Target = change.Target,
                    Detail = change.Detail,
                    Tool = change.Tool
                });
            }
            return metadata;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteCustomProperties(ZipArchive zip, string payload)
    {
        XDocument doc;
        var existing = zip.GetEntry(CustomPropsPart);
        if (existing is not null)
        {
            using (var stream = existing.Open()) doc = XDocument.Load(stream);
            existing.Delete();
        }
        else
        {
            doc = new XDocument(new XElement(CustNs + "Properties",
                new XAttribute("xmlns:vt", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes")));
        }

        // Replace any previous copy of our property, then append the new one.
        foreach (var stale in doc.Root!.Elements(CustNs + "property")
                     .Where(e => string.Equals((string?)e.Attribute("name"), PropertyName, StringComparison.Ordinal))
                     .ToList())
            stale.Remove();

        var pid = doc.Root.Elements(CustNs + "property").Count() + 2;
        doc.Root.Add(new XElement(CustNs + "property",
            new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
            new XAttribute("pid", pid),
            new XAttribute("name", PropertyName),
            new XElement(CustNs + "lpwstr", payload)));

        var entry = zip.CreateEntry(CustomPropsPart, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        doc.Save(writer);
    }

    private static void EnsureRelationshipAndContentType(ZipArchive zip)
    {
        var relsEntry = zip.GetEntry("_rels/.rels");
        if (relsEntry is not null)
        {
            XDocument rels;
            using (var stream = relsEntry.Open()) rels = XDocument.Load(stream);
            bool present = rels.Root!.Elements(RelNs + "Relationship")
                .Any(r => ((string?)r.Attribute("Type"))?.EndsWith("custom-properties", StringComparison.Ordinal) == true);
            if (!present)
            {
                relsEntry.Delete();
                var ids = rels.Root.Elements(RelNs + "Relationship")
                    .Select(r => (string?)r.Attribute("Id") ?? "")
                    .Where(id => id.StartsWith("rId", StringComparison.Ordinal) && int.TryParse(id[3..], out _))
                    .Select(id => int.Parse(id[3..]))
                    .DefaultIfEmpty(0)
                    .Max();
                rels.Root.Add(new XElement(RelNs + "Relationship",
                    new XAttribute("Type", CustomRelType),
                    new XAttribute("Target", CustomPropsPart),
                    new XAttribute("Id", $"rId{ids + 1}")));
                var replacement = zip.CreateEntry("_rels/.rels", CompressionLevel.Optimal);
                using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
                rels.Save(writer);
            }
        }

        var ctEntry = zip.GetEntry("[Content_Types].xml");
        if (ctEntry is null) return;

        XDocument ct;
        using (var stream = ctEntry.Open()) ct = XDocument.Load(stream);
        bool hasOverride = ct.Root!.Elements(CtNs + "Override")
            .Any(o => string.Equals((string?)o.Attribute("PartName"), "/" + CustomPropsPart, StringComparison.Ordinal));
        if (hasOverride) return;

        ctEntry.Delete();
        ct.Root.Add(new XElement(CtNs + "Override",
            new XAttribute("PartName", "/" + CustomPropsPart),
            new XAttribute("ContentType", CustomPropsContentType)));
        var ctReplacement = zip.CreateEntry("[Content_Types].xml", CompressionLevel.Optimal);
        using var ctWriter = new StreamWriter(ctReplacement.Open(), new UTF8Encoding(false));
        ct.Save(ctWriter);
    }

    private sealed class PersistedMetadata
    {
        public string? ModelVersion { get; set; }
        public DateTimeOffset Created { get; set; }
        public DateTimeOffset Modified { get; set; }
        public string? SourceFile { get; set; }
        public string? Generator { get; set; }
        public string? Backend { get; set; }
        public string? BackendVersion { get; set; }
        public string? Description { get; set; }
        public string? Source { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
        public List<PersistedChange> History { get; set; } = new();
    }

    private sealed class PersistedChange
    {
        public DateTimeOffset At { get; set; }
        public string? Action { get; set; }
        public string? Target { get; set; }
        public string? Detail { get; set; }
        public string? Tool { get; set; }
    }
}
