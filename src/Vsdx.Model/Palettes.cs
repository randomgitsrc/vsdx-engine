namespace Vsdx.Model;

/// <summary>
/// Named colour sets that give a diagram its visual character without changing its content.
///
/// A palette holds colours only. Deliberately: the whole point is to vary appearance while the
/// structure stays put, and a palette that also carried layout or shape choices would quietly
/// become the fixed template the brief forbids. The type makes that impossible to add by accident.
/// </summary>
public static class Palettes
{
    /// <summary>Role colours for one named palette. Missing roles fall back to <see cref="Default"/>.</summary>
    public sealed record Definition(
        string Name,
        string Default,
        IReadOnlyDictionary<string, string> Roles,
        IReadOnlyList<BandColour> Bands);

    /// <summary>
    /// Container band colours. Fill, stroke and text travel together so a band's text cannot drift
    /// out of contrast with its own background.
    /// </summary>
    public sealed record BandColour(string Fill, string Stroke, string Text);

    /// <summary>Corporate blue/green: reads as a formal system diagram.</summary>
    private static readonly Definition Enterprise = new(
        "enterprise",
        "#FFFFFF",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["client"] = "#2E75B6",
            ["actor"] = "#2E75B6",
            ["gateway"] = "#F2B705",
            ["service"] = "#70AD47",
            ["decision"] = "#F2B705",
            ["queue"] = "#C55A11",
            ["database"] = "#C55A11",
            ["storage"] = "#C55A11",
            ["monitor"] = "#7F7F7F",
            ["external"] = "#9DC3E6"
        },
        new[]
        {
            new BandColour("#EAF3FB", "#9DC3E6", "#1F4E79"),
            new BandColour("#EDF7ED", "#A9D08E", "#375623"),
            new BandColour("#FDF2E9", "#F4B183", "#833C0B"),
            new BandColour("#F2E9F7", "#C5A3D9", "#4B1F6F"),
            new BandColour("#EAF6F6", "#93CCCC", "#1F5C5C")
        });

    /// <summary>Low-saturation set for print or dense reference material.</summary>
    private static readonly Definition Muted = new(
        "muted",
        "#FFFFFF",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["client"] = "#8496B0",
            ["actor"] = "#8496B0",
            ["gateway"] = "#C9A96E",
            ["service"] = "#8FA98B",
            ["decision"] = "#C9A96E",
            ["queue"] = "#B08968",
            ["database"] = "#B08968",
            ["storage"] = "#B08968",
            ["monitor"] = "#9A9A9A",
            ["external"] = "#B8C4D4"
        },
        new[]
        {
            new BandColour("#F4F6F8", "#B8C4D4", "#33404F"),
            new BandColour("#F1F4F0", "#B9C7B4", "#3A4A36"),
            new BandColour("#F7F3EE", "#CDBBA6", "#4E4034"),
            new BandColour("#F4F1F6", "#C2B7CC", "#463B4F"),
            new BandColour("#EFF4F4", "#B2C6C6", "#354A4A")
        });

    /// <summary>Neutral greys with a single accent: the drawing recedes, annotations lead.</summary>
    private static readonly Definition Technical = new(
        "technical",
        "#FFFFFF",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["client"] = "#D9D9D9",
            ["actor"] = "#D9D9D9",
            ["gateway"] = "#FFC000",
            ["service"] = "#BFBFBF",
            ["decision"] = "#FFC000",
            ["queue"] = "#A6A6A6",
            ["database"] = "#A6A6A6",
            ["storage"] = "#A6A6A6",
            ["monitor"] = "#7F7F7F",
            ["external"] = "#EDEDED"
        },
        new[]
        {
            new BandColour("#F7F7F7", "#BFBFBF", "#262626"),
            new BandColour("#FFFFFF", "#A6A6A6", "#404040"),
            new BandColour("#EDEDED", "#8C8C8C", "#262626"),
            new BandColour("#F2F2F2", "#B3B3B3", "#333333"),
            new BandColour("#FAFAFA", "#999999", "#1A1A1A")
        });

    /// <summary>High-contrast dark set for screen-first diagrams.</summary>
    private static readonly Definition Midnight = new(
        "midnight",
        "#FFFFFF",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["client"] = "#1F3864",
            ["actor"] = "#1F3864",
            ["gateway"] = "#BF8F00",
            ["service"] = "#375623",
            ["decision"] = "#BF8F00",
            ["queue"] = "#843C0C",
            ["database"] = "#843C0C",
            ["storage"] = "#843C0C",
            ["monitor"] = "#404040",
            ["external"] = "#2F5597"
        },
        new[]
        {
            new BandColour("#DCE6F1", "#8EA9C1", "#1F3864"),
            new BandColour("#E2EFDA", "#9DBB8C", "#375623"),
            new BandColour("#FBE5D6", "#D8A47F", "#843C0C"),
            new BandColour("#E8E1F0", "#A99BC1", "#3F3151"),
            new BandColour("#DEEBF7", "#7FA8CE", "#1F4E79")
        });

    private static readonly Dictionary<string, Definition> All =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Enterprise.Name] = Enterprise,
            [Muted.Name] = Muted,
            [Technical.Name] = Technical,
            [Midnight.Name] = Midnight
        };

    /// <summary>Names a caller may ask for. Used in messages so the list is never stale.</summary>
    public static IReadOnlyCollection<string> Names => All.Keys;

    public static bool TryGet(string? name, out Definition palette)
    {
        palette = Enterprise;
        return !string.IsNullOrWhiteSpace(name) && All.TryGetValue(name, out palette!);
    }
}
