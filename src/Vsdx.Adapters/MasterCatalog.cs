using OfficeIMO;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Maps the JSON model's shape-kind vocabulary onto Visio masters that OfficeIMO.Visio can
/// generate natively, and converts geometry units and enum values in both directions.
/// </summary>
public static class MasterCatalog
{
    /// <summary>Built-in masters OfficeIMO.Visio can generate without an external stencil.</summary>
    public static readonly IReadOnlySet<string> BuiltIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Rectangle", "Ellipse", "Square", "Circle", "Dynamic connector", "Diamond", "Triangle",
        "Process", "Decision", "Data", "Preparation", "Manual operation", "Off-page reference",
        "Parallelogram", "Hexagon", "Trapezoid", "Pentagon", "Text Box", "Container"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["box"] = "Rectangle", ["rect"] = "Rectangle", ["rectangle"] = "Rectangle",
        ["rounded"] = "Rectangle", ["roundedrect"] = "Rectangle", ["card"] = "Rectangle",
        ["service"] = "Rectangle", ["component"] = "Rectangle", ["system"] = "Rectangle",
        ["database"] = "Process", ["db"] = "Process", ["store"] = "Process", ["storage"] = "Process",
        ["datastore"] = "Process", ["server"] = "Process", ["cylinder"] = "Process",
        ["queue"] = "Process", ["topic"] = "Process", ["bus"] = "Process",
        ["circle"] = "Circle", ["ellipse"] = "Ellipse", ["oval"] = "Ellipse",
        ["actor"] = "Circle", ["user"] = "Circle", ["person"] = "Circle",
        ["square"] = "Square",
        ["diamond"] = "Diamond", ["decision"] = "Decision", ["condition"] = "Decision",
        ["start"] = "Process", ["end"] = "Process", ["terminator"] = "Process",
        ["process"] = "Process", ["task"] = "Process", ["step"] = "Process",
        ["data"] = "Data", ["io"] = "Data", ["document"] = "Rectangle",
        ["preparation"] = "Preparation", ["setup"] = "Preparation",
        ["manual"] = "Manual operation", ["manualoperation"] = "Manual operation",
        ["offpage"] = "Off-page reference", ["link"] = "Off-page reference",
        ["parallelogram"] = "Parallelogram", ["hexagon"] = "Hexagon",
        ["trapezoid"] = "Trapezoid", ["pentagon"] = "Pentagon",
        ["text"] = "Text Box", ["textbox"] = "Text Box", ["label"] = "Text Box",
        ["note"] = "Text Box", ["annotation"] = "Text Box",
        ["container"] = "Container", ["group"] = "Container", ["zone"] = "Container",
        ["layer"] = "Container", ["boundary"] = "Container", ["region"] = "Container",
        ["network"] = "Rectangle", ["router"] = "Process", ["switch"] = "Process",
        ["firewall"] = "Hexagon", ["cloud"] = "Ellipse", ["gateway"] = "Hexagon",
        ["triangle"] = "Triangle",
    };

    /// <summary>Resolves a model shape kind to a built-in Visio master NameU.</summary>
    public static string ResolveMaster(string? shape)
    {
        if (string.IsNullOrWhiteSpace(shape)) return "Rectangle";
        var key = shape.Trim().Replace(" ", "").Replace("_", "").Replace("-", "");
        if (Aliases.TryGetValue(key, out var mapped)) return mapped;
        // Already an exact master name?
        foreach (var name in BuiltIn)
            if (string.Equals(name, shape.Trim(), StringComparison.OrdinalIgnoreCase))
                return name;
        return "Rectangle";
    }

    public static bool IsCaseSensitiveMaster(string master) =>
        master.Equals("Text Box", StringComparison.Ordinal);

    public static OfficeLength Inches(double value) => OfficeLength.Inches(value);

    public static double ToInches(OfficeLength length) => length.ToInches();

    /// <summary>Converts a page size unit string to its inch factor.</summary>
    public static double UnitFactor(string? unit) => (unit ?? "inches").ToLowerInvariant() switch
    {
        "cm" or "centimeter" or "centimeters" => 1.0 / 2.54,
        "mm" or "millimeter" or "millimeters" => 1.0 / 25.4,
        "pt" or "point" or "points" => 1.0 / 72.0,
        _ => 1.0
    };

    public static string ToModelKind(string? masterNameU) => masterNameU switch
    {
        null => "Rectangle",
        "Diamond" or "Decision" => "Diamond",
        "Circle" or "Ellipse" => "Circle",
        "Square" => "Square",
        "Process" => "Process",
        "Data" => "Data",
        "Preparation" => "Preparation",
        "Manual operation" => "ManualOperation",
        "Off-page reference" => "OffPageReference",
        "Parallelogram" => "Parallelogram",
        "Hexagon" => "Hexagon",
        "Trapezoid" => "Trapezoid",
        "Pentagon" => "Pentagon",
        "Triangle" => "Triangle",
        "Text Box" => "Text",
        "Container" => "Container",
        _ => "Rectangle"
    };
}
