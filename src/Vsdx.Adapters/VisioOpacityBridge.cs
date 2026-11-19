using System.IO.Compression;
using System.Xml.Linq;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Writes shape transparency into the page part.
///
/// `ShapeStyle.Opacity` is declared on the model and permitted by the schema, but OfficeIMO has no transparency API
/// — `VisioShape` offers fill, stroke, their patterns and a few text setters, and nothing else. So the writer
/// reported the field as unsupported and the caller was told, correctly, that setting it had no effect.
///
/// Visio does support it: `FillForegndTrans` and `LineColorTrans` are ordinary cells, 0 for opaque and 1 for
/// invisible. This project already writes the page XML directly for free paths, so the same route works here.
///
/// **The inversion matters**: `Opacity = 1` is fully visible, free paths in this project already use 0..1 with 1 as
/// opaque, and Visio's `*Trans` is the opposite. Getting it backwards would make a faded component the most
/// prominent thing on the page.
/// </summary>
public static class VisioOpacityBridge
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/office/visio/2012/main";

    /// <summary>Applies opacity to every shape on the page, and returns how many were touched.</summary>
    public static int Apply(Diagram diagram, string vsdxPath, CompatibilityReport compat)
    {
        var faded = diagram.Pages
            .SelectMany(p => p.Nodes.Select(n => (Id: n.Id, Style: n.Style))
                .Concat(p.Groups.Select(g => (Id: g.Id, Style: g.Style))))
            .Where(x => x.Style?.Opacity is double o && Math.Abs(o - 1.0) > 0.001)
            .ToDictionary(x => x.Id, x => x.Style!.Opacity!.Value, StringComparer.Ordinal);

        if (faded.Count == 0) return 0;

        using var zip = ZipFile.Open(vsdxPath, ZipArchiveMode.Update);

        int touched = 0;

        foreach (var entry in zip.Entries
                     .Where(e => e.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)
                              && e.FullName != "visio/pages/pages.xml")
                     .ToList())
        {
            XDocument doc;

            using (var stream = entry.Open())
                doc = XDocument.Load(stream);

            bool changed = false;

            foreach (var shape in doc.Descendants(Ns + "Shape"))
            {
                var id = (string?)shape.Attribute("NameU") is null
                    ? (string?)shape.Attribute("ID")
                    : null;

                // Shapes are matched by the model id the writer stamped, not the Visio shape id: those differ after
                // any rename, and `VsdxModelId` is the stable handle everything else uses.
                // Two different mechanisms carry the model id, depending on what kind of shape this is:
                //   nodes      -> User section, Row N="VsdxModelId"
                //   containers -> Prop section, Row N="OfficeIMOOriginalId"
                // Reading only the first found no containers at all, so a faded band was never matched.
                var modelId = LookupRow(shape, "User", "VsdxModelId")
                           ?? LookupRow(shape, "Prop", "OfficeIMOOriginalId");

                if (modelId is null || !faded.TryGetValue(modelId, out var opacity)) continue;

                // Visio is inverted: transparency = 1 - opacity.
                string trans = (1.0 - opacity).ToString("0.####",
                    System.Globalization.CultureInfo.InvariantCulture);

                shape.Add(
                    new XElement(Ns + "Cell", new XAttribute("N", "FillForegndTrans"), new XAttribute("V", trans)),
                    new XElement(Ns + "Cell", new XAttribute("N", "LineColorTrans"), new XAttribute("V", trans)));

                changed = true;
                touched++;
            }

            if (!changed) continue;

            using var output = entry.Open();
            output.SetLength(0);
            using var writer = new StreamWriter(output);
            doc.Save(writer);
        }

        return touched;
    }

    /// <summary>Reads the `Value` cell of a named row inside a named section, or null when absent.</summary>
    private static string? LookupRow(XElement shape, string section, string row) =>
        (string?)shape.Elements(Ns + "Section")
            .Where(s => (string?)s.Attribute("N") == section)
            .Elements(Ns + "Row")
            .Where(r => (string?)r.Attribute("N") == row)
            .Elements(Ns + "Cell")
            .FirstOrDefault(c => (string?)c.Attribute("N") == "Value")
            ?.Attribute("V");
}
