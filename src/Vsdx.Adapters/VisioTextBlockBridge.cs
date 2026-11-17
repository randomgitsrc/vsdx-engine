using System.IO.Compression;
using System.Xml.Linq;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Writes standalone text (titles, subtitles, captions, legends) into the page part.
///
/// OfficeIMO.Visio's API is shape- and connector-oriented and has no free text primitive that
/// carries our role semantics, so these are emitted as native <c>Text Box</c> shapes. They stay
/// editable in Visio and are ordinary shapes to any other consumer.
/// </summary>
public static class VisioTextBlockBridge
{
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";

    /// <summary>
    /// Point size per semantic role, matching <c>TypeScale</c> defaults.
    ///
    /// Adapters cannot reference the Engine (the dependency runs the other way), so the mapping is
    /// duplicated here deliberately. A caller that wants the canvas-derived scale sets
    /// <see cref="ShapeStyle.FontSize"/> explicitly, which always wins.
    /// </summary>
    public static double SizeForRole(string? role) => role?.ToLowerInvariant() switch
    {
        "title" => 20,
        "subtitle" => 12,
        "caption" => 9,
        "legend" => 10,
        "note" => 8,
        _ => 10
    };

    /// <summary>Smallest size that stays legible in print.</summary>
    public const double MinReadablePt = 8.0;

    public static int Apply(Diagram diagram, string vsdxPath, CompatibilityReport? compat = null)
    {
        int written = 0;

        for (int pageIndex = 0; pageIndex < diagram.Pages.Count; pageIndex++)
        {
            var pageModel = diagram.Pages[pageIndex];
            if (pageModel.TextBlocks.Count == 0) continue;

            written += ApplyToPage(vsdxPath, pageIndex, pageModel, diagram, compat);
        }

        return written;
    }

    private static int ApplyToPage(
        string vsdxPath, int pageIndex, Page pageModel, Diagram diagram, CompatibilityReport? compat)
    {
        var entryName = $"visio/pages/page{pageIndex + 1}.xml";

        using var zip = ZipFile.Open(vsdxPath, ZipArchiveMode.Update);
        var entry = zip.GetEntry(entryName);
        if (entry is null) return 0;

        XDocument doc;
        using (var stream = entry.Open()) doc = XDocument.Load(stream);

        var shapesContainer = doc.Descendants(VisioNs + "Shapes").FirstOrDefault();
        if (shapesContainer is null) return 0;

        var usedIds = new HashSet<int>(
            doc.Descendants(VisioNs + "Shape")
                .Select(s => (string?)s.Attribute("ID"))
                .Where(id => id is not null && int.TryParse(id, out _))
                .Select(id => int.Parse(id!)));

        // Text blocks are engine-owned: drop the previous generation so an edit replaces it
        // instead of stacking copies, the same rule FreePath follows.
        foreach (var shape in doc.Descendants(VisioNs + "Shape").ToList())
        {
            if ((string?)shape.Attribute("NameU") != "TextBlock") continue;
            var id = (string?)shape.Attribute("ID");
            shape.Remove();
            if (id is not null && int.TryParse(id, out int n)) usedIds.Remove(n);
        }

        usedIds = new HashSet<int>(
            doc.Descendants(VisioNs + "Shape")
                .Select(s => (string?)s.Attribute("ID"))
                .Where(id => id is not null && int.TryParse(id, out _))
                .Select(id => int.Parse(id!)));

        int nextId = usedIds.Count == 0 ? 1 : usedIds.Max() + 1;
        int written = 0;
        foreach (var block in pageModel.TextBlocks)
        {
            if (string.IsNullOrEmpty(block.Text)) continue;

            while (usedIds.Contains(nextId)) nextId++;
            var shape = BuildShape(block, nextId);
            usedIds.Add(nextId);
            shapesContainer.Add(shape);
            written++;
        }

        if (written == 0) return 0;

        entry.Delete();
        var replacement = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(replacement.Open(), new System.Text.UTF8Encoding(false));
        doc.Save(writer);

        compat?.Info("write.text-block", $"写入了 {written} 个独立文本块", pageModel.Name);
        return written;
    }

    private static XElement BuildShape(TextBlock block, int id)
    {
        var shape = new XElement(VisioNs + "Shape",
            new XAttribute("ID", id),
            new XAttribute("NameU", "TextBlock"),
            new XAttribute("Type", "Shape"),
            new XAttribute("LineStyle", "0"),
            new XAttribute("FillStyle", "0"),
            new XAttribute("TextStyle", "0"));

        // Name carries the model id so the reader can recover the semantic role and identity.
        // Falling back to the id keeps the round-trip lossless when the caller set no name.
        shape.SetAttributeValue("Name", string.IsNullOrEmpty(block.Name) ? block.Id : block.Name);

        AddCell(shape, "PinX", Num(block.Bounds.X));
        AddCell(shape, "PinY", Num(block.Bounds.Y));
        AddCell(shape, "Width", Num(block.Bounds.W));
        AddCell(shape, "Height", Num(block.Bounds.H));
        AddCell(shape, "LocPinX", Num(block.Bounds.W / 2));
        AddCell(shape, "LocPinY", Num(block.Bounds.H / 2));

        // A text block is text only: no fill, no border, so it reads as typography.
        AddCell(shape, "FillPattern", "0");
        AddCell(shape, "LinePattern", "0");

        var style = block.Style;
        if (style?.TextColor is not null) AddCell(shape, "Color", style.TextColor);
        if (style?.Font is not null) AddCell(shape, "Font", style.Font);

        double size = style?.FontSize ?? SizeForRole(block.Role);
        size = Math.Max(size, MinReadablePt);

        var character = new XElement(VisioNs + "Section", new XAttribute("N", "Character"));
        character.Add(new XElement(VisioNs + "Row", new XAttribute("IX", "0"),
            new XElement(VisioNs + "Cell",
                new XAttribute("N", "Size"),
                new XAttribute("V", Num(size / 72.0)),
                new XAttribute("U", "PT")),
            new XElement(VisioNs + "Cell", new XAttribute("N", "Style"),
                new XAttribute("V", style?.Italic == true ? "2" : "0"))));
        if (style?.Bold == true)
            character.Descendants(VisioNs + "Cell")
                .First(c => (string?)c.Attribute("N") == "Style")
                .SetAttributeValue("V", style.Italic == true ? "3" : "1");
        shape.Add(character);

        // Alignment lives in the paragraph section.
        string align = block.Align?.ToLowerInvariant() switch
        {
            "center" => "1",
            "right" => "2",
            _ => "0"
        };
        shape.Add(new XElement(VisioNs + "Section", new XAttribute("N", "Paragraph"),
            new XElement(VisioNs + "Row", new XAttribute("IX", "0"),
                new XElement(VisioNs + "Cell", new XAttribute("N", "HorzAlign"),
                    new XAttribute("V", align)))));

        // Vertical anchor at the top: a title should not float in the middle of its box.
        AddCell(shape, "VerticalAlign", "0");

        if (block.NativeCells is { Count: > 0 })
            foreach (var (name, value) in block.NativeCells)
                AddCell(shape, name, value);

        shape.Add(new XElement(VisioNs + "Text", block.Text));

        // Stable id, matching the node and path convention.
        shape.Add(new XElement(VisioNs + "Section",
            new XAttribute("N", "User"),
            new XElement(VisioNs + "Row",
                new XAttribute("N", "VsdxModelId"),
                new XElement(VisioNs + "Cell",
                    new XAttribute("N", "Value"), new XAttribute("V", block.Id)),
                new XElement(VisioNs + "Cell",
                    new XAttribute("N", "Prompt"),
                    new XAttribute("V", "Agent VSDX Engine stable text block id")))));

        return shape;
    }

    private static void AddCell(XElement shape, string name, string value) =>
        shape.Add(new XElement(VisioNs + "Cell",
            new XAttribute("N", name), new XAttribute("V", value)));

    private static string Num(double value) =>
        Math.Round(value, 6).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
