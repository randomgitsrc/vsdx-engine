using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every role colour must reach WCAG AA against the text drawn on it.
///
/// Three colours sat at 4.36:1 — `enterprise`'s database, queue and storage, all `#C55A11` on
/// `#111111` — which is 0.14 short. The shortfall was recorded in `docs/phase8/palette-contrast.md`
/// and left open as a "design decision" for several rounds.
///
/// It was not a design decision. The text colour is already near-black, so only the fill could move,
/// and lifting it by 3/255 reaches 4.52 — a change no reader can see. Leaving a measurable accessibility
/// failure in place because the fix is small is the wrong trade.
///
/// AA is the floor, not a target: 4.5:1 for normal text is the published criterion, so this needs no
/// owner ruling to be actionable.
/// </summary>
public sealed class PaletteContrastFloorTests
{
    /// <summary>WCAG 2.1 AA for normal-size text.</summary>
    private const double AaNormalText = 4.5;

    /// <summary>WCAG 2.1 AA for large text.</summary>
    private const double AaLargeText = 3.0;

    private static double Channel(double c) =>
        c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double Luminance(string hex)
    {
        hex = hex.TrimStart('#');

        double R = Convert.ToInt32(hex[..2], 16) / 255.0;
        double G = Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0;
        double B = Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0;

        return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);

        if (la < lb) (la, lb) = (lb, la);

        return (la + 0.05) / (lb + 0.05);
    }

    [Fact]
    public void EveryRoleColourMeetsAa()
    {
        var failures = new List<string>();

        foreach (var name in Palettes.Names)
        {
            Palettes.TryGet(name, out var palette);

            foreach (var (role, fill) in palette.Roles)
            {
                string text = ColorUtil.ReadableTextColor(fill);
                double ratio = Contrast(fill, text);

                if (ratio < AaNormalText)
                    failures.Add($"{name}:{role} {fill} on {text} = {ratio:F2}");
            }
        }

        Assert.True(failures.Count == 0,
            $"below WCAG AA ({AaNormalText}:1): {string.Join(", ", failures)}");
    }

    [Fact]
    public void ThePreviouslyFailingColoursNowPass()
    {
        // The specific three, pinned so a regression names them.
        Palettes.TryGet("enterprise", out var enterprise);

        foreach (var role in new[] { "database", "queue", "storage" })
        {
            var fill = enterprise.Roles[role];
            double ratio = Contrast(fill, ColorUtil.ReadableTextColor(fill));

            Assert.True(ratio >= AaNormalText, $"{role} at {ratio:F2}");
        }
    }

    [Fact]
    public void TheFixStayedVisuallySmall()
    {
        // A colour that reaches AA by becoming a different colour is not a fix. The original was
        // #C55A11; the change must stay within a few units per channel.
        Palettes.TryGet("enterprise", out var enterprise);
        var fill = enterprise.Roles["database"].TrimStart('#');

        int r = Convert.ToInt32(fill[..2], 16);
        int g = Convert.ToInt32(fill.Substring(2, 2), 16);
        int b = Convert.ToInt32(fill.Substring(4, 2), 16);

        Assert.InRange(r, 0xC5, 0xCF);
        Assert.InRange(g, 0x5A, 0x62);
        Assert.InRange(b, 0x11, 0x1A);
    }

    [Fact]
    public void LargeTextNeedsLessAndNothingFallsBelowIt()
    {
        // Band fills carry band headings at a larger size; nothing may sit under the 3:1 floor.
        foreach (var name in Palettes.Names)
        {
            Palettes.TryGet(name, out var palette);

            foreach (var (role, fill) in palette.Roles)
            {
                double ratio = Contrast(fill, ColorUtil.ReadableTextColor(fill));

                Assert.True(ratio >= AaLargeText, $"{name}:{role} = {ratio:F2} below the 3:1 floor");
            }
        }
    }
}
