using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every palette fill must carry readable text.
///
/// Measured with the WCAG relative-luminance formula across all four palettes:
///
///   container bands   6.56 to 16.13    all pass AA (4.5:1)
///   default fill     18.88             passes
///   role fills        4.36 to 16.13   three fail AA, all the same colour
///
/// The three are `enterprise`'s `database`, `queue` and `storage`, which share `#C55A11` with
/// `#111111` text at 4.36:1. That is above AA-large (3:1) but below AA for body text.
///
/// The threshold is applied to bands and the default at AA, and to role fills at AA-large with the
/// failing three pinned explicitly: they were chosen as a set with the rest of the palette, and
/// nudging one colour changes how the diagram reads. Raising them is a design decision, so this test
/// records the number rather than silently redefying a palette.
/// </summary>
public sealed class PaletteContrastTests
{
    /// <summary>WCAG 2.1 AA for normal text.</summary>
    private const double AA = 4.5;

    /// <summary>WCAG 2.1 AA for large text (18pt, or 14pt bold).</summary>
    private const double AALarge = 3.0;

    /// <summary>
    /// Empty: every role colour now meets AA. This list used to name the three that did not —
    /// `enterprise`'s database, queue and storage at 4.36:1 on #111111 — and served as a baseline so a
    /// new entry could not land below AA unnoticed. It stays as the mechanism, with nothing in it.
    /// </summary>
    private static readonly string[] KnownBelowAa = Array.Empty<string>();

    private static (double R, double G, double B) Parse(string hex)
    {
        var h = hex.TrimStart('#');
        return (Convert.ToInt32(h.Substring(0, 2), 16) / 255.0,
                Convert.ToInt32(h.Substring(2, 2), 16) / 255.0,
                Convert.ToInt32(h.Substring(4, 2), 16) / 255.0);
    }

    private static double Linear(double c) =>
        c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double Luminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    private static double Ratio(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        if (la < lb) (la, lb) = (lb, la);
        return (la + 0.05) / (lb + 0.05);
    }

    public static TheoryData<string> PaletteNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Palettes.Names.OrderBy(x => x, StringComparer.Ordinal)) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(PaletteNames))]
    public void BandTextMeetsAa(string paletteName)
    {
        Assert.True(Palettes.TryGet(paletteName, out var palette));

        foreach (var band in palette.Bands)
        {
            double ratio = Ratio(band.Fill, band.Text);

            Assert.True(ratio >= AA,
                $"{paletteName}: band {band.Fill} on {band.Text} is {ratio:F2}, below AA {AA}");
        }
    }

    [Theory]
    [MemberData(nameof(PaletteNames))]
    public void TheDefaultFillMeetsAa(string paletteName)
    {
        Assert.True(Palettes.TryGet(paletteName, out var palette));

        var text = ColorUtil.ReadableTextColor(palette.Default);
        double ratio = Ratio(palette.Default, text);

        Assert.True(ratio >= AA, $"{paletteName}: default {palette.Default} on {text} is {ratio:F2}");
    }

    [Theory]
    [MemberData(nameof(PaletteNames))]
    public void RoleTextIsReadableEvenWhereItMissesAa(string paletteName)
    {
        Assert.True(Palettes.TryGet(paletteName, out var palette));

        foreach (var (role, fill) in palette.Roles)
        {
            var text = ColorUtil.ReadableTextColor(fill);
            double ratio = Ratio(fill, text);

            Assert.True(ratio >= AALarge,
                $"{paletteName}:{role} {fill} on {text} is {ratio:F2}, below AA-large {AALarge}");
        }
    }

    [Fact]
    public void NoRoleColourFallsBelowAa()
    {
        // The baseline is empty: a new palette entry that lands below AA fails here by name.
        var below = new List<string>();

        foreach (var name in Palettes.Names)
        {
            Assert.True(Palettes.TryGet(name, out var palette));

            foreach (var (role, fill) in palette.Roles)
            {
                var text = ColorUtil.ReadableTextColor(fill);
                if (Ratio(fill, text) < AA) below.Add($"{name}:{role}");
            }
        }

        Assert.Equal(KnownBelowAa.OrderBy(x => x, StringComparer.Ordinal),
                     below.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void TheLuminanceFormulaMatchesKnownValues()
    {
        // White on black is 21:1 by definition; a broken formula would still make the other tests
        // pass if it were merely scaled wrong.
        Assert.Equal(21.0, Ratio("#FFFFFF", "#000000"), 2);
        Assert.Equal(1.0, Ratio("#777777", "#777777"), 3);
    }
}
