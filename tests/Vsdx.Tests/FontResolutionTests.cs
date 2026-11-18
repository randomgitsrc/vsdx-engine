using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Renderer;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The renderer must draw in the font the file asks for.
///
/// It resolved fonts purely from a caller-supplied file or the first available candidate, never
/// from the VSDX. Measured: the same diagram with Georgia, Courier New, and a font that does not
/// exist produced byte-identical PNGs. A preview that ignores the font is a preview that disagrees
/// with the deliverable — which is exactly what the project's rendering requirement forbids.
/// </summary>
public sealed class FontResolutionTests
{
    // ---- Font file inspection -------------------------------------------------

    [Fact]
    public void ReadsTheFamilyNameFromATrueTypeFile()
    {
        // Verified against a real file rather than a synthetic fixture: the name table is a public
        // binary format, and this is the only way to know the parser agrees with real fonts.
        var path = FindAnyFontFile();
        if (path is null) return; // no system fonts in this environment; nothing to assert

        var family = FontResolver.ReadFamilyName(path);

        Assert.False(string.IsNullOrWhiteSpace(family));
        Assert.DoesNotContain('\0', family!);
    }

    [Fact]
    public void RejectsFilesThatAreNotFonts()
    {
        var fake = Path.Combine(Path.GetTempPath(), $"not-a-font-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(fake, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });

        try
        {
            Assert.Null(FontResolver.ReadFamilyName(fake));
        }
        finally
        {
            File.Delete(fake);
        }
    }

    [Fact]
    public void MissingFileYieldsNullRatherThanThrowing()
    {
        Assert.Null(FontResolver.ReadFamilyName(Path.Combine(Path.GetTempPath(), "no-such-font-xyz.ttf")));
    }

    // ---- Resolving a requested family ----------------------------------------

    [Fact]
    public void ResolvesAFamilyNameToARealFontFile()
    {
        var resolver = new FontResolver();

        // DejaVu is near-universal on Linux and is in the resolver's own candidate list.
        var resolved = resolver.ResolveByFamily("DejaVu Sans", requiresCjk: false);

        if (resolved is null) return; // family genuinely absent on this machine

        Assert.True(File.Exists(resolved));
    }

    [Fact]
    public void AnUnknownFamilyDoesNotResolveToSomethingArbitrary()
    {
        var resolver = new FontResolver();

        // Returning some other font would be worse than returning nothing: the caller would
        // believe the request was honoured.
        var resolved = resolver.ResolveByFamily("NoSuchFontFamily-XYZ", requiresCjk: false);

        Assert.Null(resolved);
    }

    [Fact]
    public void ExplicitFontFileStillWins()
    {
        var path = FindAnyFontFile();
        if (path is null) return;

        var resolver = new FontResolver();

        // A caller who names a file has made a decision; it outranks anything read from the file.
        Assert.Equal(path, resolver.Resolve(requiresCjk: false, preferredFontFile: path));
    }

    private static string? FindAnyFontFile()
    {
        foreach (var dir in new[] { "/usr/share/fonts", "/usr/local/share/fonts" })
        {
            if (!Directory.Exists(dir)) continue;
            var hit = Directory.EnumerateFiles(dir, "*.ttf", SearchOption.AllDirectories).FirstOrDefault();
            if (hit is not null) return hit;
        }
        return null;
    }
}
