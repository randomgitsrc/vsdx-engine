using System.Globalization;
using System.Text.RegularExpressions;

namespace Vsdx.Model;

/// <summary>Colour helpers shared by adapters and renderers.</summary>
public static class ColorUtil
{
    private static readonly Regex Hex = new("^#?(?<hex>[0-9a-fA-F]{6})$", RegexOptions.Compiled);
    private static readonly Regex HexShort = new("^#?(?<hex>[0-9a-fA-F]{3})$", RegexOptions.Compiled);
    private static readonly Regex Rgb = new(@"^rgba?\((?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Normalises a colour to <c>#RRGGBB</c> upper case, or returns null.</summary>
    public static string? Normalize(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        var value = color.Trim();
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        var m = Hex.Match(value);
        if (m.Success) return "#" + m.Groups["hex"].Value.ToUpperInvariant();

        var s = HexShort.Match(value);
        if (s.Success)
        {
            var h = s.Groups["hex"].Value;
            return $"#{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}".ToUpperInvariant();
        }

        var r = Rgb.Match(value);
        if (r.Success)
        {
            int rv = int.Parse(r.Groups["r"].Value, CultureInfo.InvariantCulture);
            int gv = int.Parse(r.Groups["g"].Value, CultureInfo.InvariantCulture);
            int bv = int.Parse(r.Groups["b"].Value, CultureInfo.InvariantCulture);
            return $"#{Clamp(rv):X2}{Clamp(gv):X2}{Clamp(bv):X2}";
        }

        if (NamedColors.TryGetValue(value, out var named)) return named;
        return null;
    }

    public static bool TryParse(string? color, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var normalized = Normalize(color);
        if (normalized is null || normalized.Length != 7) return false;
        r = Convert.ToByte(normalized.Substring(1, 2), 16);
        g = Convert.ToByte(normalized.Substring(3, 2), 16);
        b = Convert.ToByte(normalized.Substring(5, 2), 16);
        return true;
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>Blends toward white; amount 0..1.</summary>
    public static string Tint(string color, double amount)
    {
        if (!TryParse(color, out var r, out var g, out var b)) return color;
        amount = Math.Clamp(amount, 0, 1);
        return ToHex(Mix(r, 255, amount), Mix(g, 255, amount), Mix(b, 255, amount));
    }

    /// <summary>Blends toward black; amount 0..1.</summary>
    public static string Shade(string color, double amount)
    {
        if (!TryParse(color, out var r, out var g, out var b)) return color;
        amount = Math.Clamp(amount, 0, 1);
        return ToHex(Mix(r, 0, amount), Mix(g, 0, amount), Mix(b, 0, amount));
    }

    /// <summary>Relative luminance 0..1, used to pick readable foreground text.</summary>
    public static double Luminance(string color)
    {
        if (!TryParse(color, out var r, out var g, out var b)) return 1.0;
        static double Channel(byte v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    /// <summary>Contrast ratio between two colours (WCAG).</summary>
    public static double Contrast(string a, string b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    public static string ReadableTextColor(string background) =>
        Contrast(background, "#FFFFFF") >= Contrast(background, "#111111") ? "#FFFFFF" : "#111111";

    private static byte Mix(byte from, byte to, double amount)
        => (byte)Math.Round(from + (to - from) * amount, MidpointRounding.AwayFromZero);

    private static int Clamp(int v) => Math.Clamp(v, 0, 255);

    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["white"] = "#FFFFFF", ["black"] = "#000000", ["red"] = "#FF0000", ["green"] = "#008000",
        ["blue"] = "#0000FF", ["yellow"] = "#FFFF00", ["orange"] = "#FFA500", ["purple"] = "#800080",
        ["gray"] = "#808080", ["grey"] = "#808080", ["silver"] = "#C0C0C0", ["navy"] = "#000080",
        ["teal"] = "#008080", ["cyan"] = "#00FFFF", ["magenta"] = "#FF00FF", ["maroon"] = "#800000",
        ["olive"] = "#808000", ["lime"] = "#00FF00", ["aqua"] = "#00FFFF", ["fuchsia"] = "#FF00FF",
        ["cornflowerblue"] = "#6495ED", ["lightskyblue"] = "#87CEFA", ["steelblue"] = "#4682B4",
        ["darkblue"] = "#00008B", ["lightgray"] = "#D3D3D3", ["lightgrey"] = "#D3D3D3",
        ["darkgray"] = "#A9A9A9", ["darkgrey"] = "#A9A9A9", ["whitesmoke"] = "#F5F5F5",
        ["aliceblue"] = "#F0F8FF", ["goldenrod"] = "#DAA520", ["crimson"] = "#DC143C",
        ["tomato"] = "#FF6347", ["seagreen"] = "#2E8B57", ["forestgreen"] = "#228B22",
        ["dodgerblue"] = "#1E90FF", ["royalblue"] = "#4169E1", ["slategray"] = "#708090",
        ["slategrey"] = "#708090", ["gainsboro"] = "#DCDCDC", ["beige"] = "#F5F5DC",
        ["ivory"] = "#FFFFF0", ["khaki"] = "#F0E68C", ["salmon"] = "#FA8072",
        ["darkorange"] = "#FF8C00", ["hotpink"] = "#FF69B4", ["orchid"] = "#DA70D6",
        ["plum"] = "#DDA0DD", ["tan"] = "#D2B48C", ["wheat"] = "#F5DEB3",
        ["chocolate"] = "#D2691E", ["sienna"] = "#A0522D", ["peru"] = "#CD853F",
    };
}
