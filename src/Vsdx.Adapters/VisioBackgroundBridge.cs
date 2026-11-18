using System.IO.Compression;
using System.Xml.Linq;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Writes each page's background colour into the package.
///
/// The colour belongs in the page's <c>PageSheet</c>, which lives in <c>visio/pages/pages.xml</c> —
/// not in the per-page <c>pageN.xml</c> that the other bridges rewrite. Keeping it out of those
/// parts is why this can run after them without any ordering constraint.
///
/// The engine previously set <c>Diagram.Background</c> and <c>Page.Background</c> on the model and
/// claimed the step had been applied, while nothing wrote either one: the exported model returned
/// null and the page XML had no PageBackground cell. The renderer did honour a background, but only
/// from its own request parameter, so a colour set through the spec produced a white document and a
/// white preview.
/// </summary>
public static class VisioBackgroundBridge
{
    private const string PagesPart = "visio/pages/pages.xml";
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";

    /// <summary>Writes page backgrounds recorded on the model. Returns how many were written.</summary>
    public static int Apply(Diagram diagram, string vsdxPath)
    {
        var wanted = diagram.Pages
            .Where(p => !string.IsNullOrWhiteSpace(p.Background))
            .Select(p => p.Background!)
            .ToList();

        if (wanted.Count == 0) return 0;

        try
        {
            using var zip = ZipFile.Open(vsdxPath, ZipArchiveMode.Update);
            var entry = zip.GetEntry(PagesPart);
            if (entry is null) return 0;

            XDocument doc;
            using (var stream = entry.Open()) doc = XDocument.Load(stream);

            var sheets = doc.Descendants(VisioNs + "PageSheet").ToList();
            if (sheets.Count == 0) return 0;

            int written = 0;
            for (int i = 0; i < diagram.Pages.Count && i < sheets.Count; i++)
            {
                var colour = diagram.Pages[i].Background;
                if (string.IsNullOrWhiteSpace(colour)) continue;

                if (!ColorUtil.TryParse(colour, out _, out _, out _)) continue;

                SetCell(sheets[i], "PageBackground", colour);
                written++;
            }

            if (written == 0) return 0;

            entry.Delete();
            var replacement = zip.CreateEntry(PagesPart, CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open(), new System.Text.UTF8Encoding(false));
            doc.Save(writer);

            return written;
        }
        catch (IOException) { return 0; }
        catch (InvalidDataException) { return 0; }
    }

    /// <summary>Replaces the cell when present, appends it otherwise.</summary>
    private static void SetCell(XElement sheet, string name, string value)
    {
        var existing = sheet.Elements(VisioNs + "Cell")
            .FirstOrDefault(c => (string?)c.Attribute("N") == name);

        if (existing is not null)
        {
            existing.SetAttributeValue("V", value);
            return;
        }

        sheet.Add(new XElement(VisioNs + "Cell",
            new XAttribute("N", name), new XAttribute("V", value)));
    }

    /// <summary>Reads a page background back out of the package, or null.</summary>
    public static string? Read(string vsdxPath, int pageIndex)
    {
        try
        {
            using var zip = ZipFile.OpenRead(vsdxPath);
            var entry = zip.GetEntry(PagesPart);
            if (entry is null) return null;

            XDocument doc;
            using (var stream = entry.Open()) doc = XDocument.Load(stream);

            var sheets = doc.Descendants(VisioNs + "PageSheet").ToList();
            if (pageIndex < 0 || pageIndex >= sheets.Count) return null;

            var cell = sheets[pageIndex].Elements(VisioNs + "Cell")
                .FirstOrDefault(c => (string?)c.Attribute("N") == "PageBackground");

            var value = (string?)cell?.Attribute("V");
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (IOException) { return null; }
        catch (InvalidDataException) { return null; }
    }
}
