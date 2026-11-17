namespace Vsdx.Renderer;

/// <summary>
/// Resolves a TrueType/OpenType font file for raster text so that CJK glyphs render as
/// real outlines instead of missing-glyph boxes. OfficeIMO's built-in fallback face has no
/// CJK coverage, so an explicit font file is required for Chinese/Japanese/Korean text.
/// </summary>
public sealed class FontResolver
{
    private static readonly string[] CjkCandidates =
    {
        // Linux
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/opentype/noto/NotoSansCJKsc-Regular.otf",
        "/usr/share/fonts/truetype/noto/NotoSansSC-Regular.ttf",
        "/usr/local/share/fonts/NotoSansSC-VF.ttf",
        "/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc",
        "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc",
        "/usr/share/fonts/truetype/arphic/uming.ttc",
        // Windows (also reachable from WSL)
        "/mnt/c/Windows/Fonts/msyh.ttc",
        "/mnt/c/Windows/Fonts/msyhbd.ttc",
        "/mnt/c/Windows/Fonts/simsun.ttc",
        "/mnt/c/Windows/Fonts/Deng.ttf",
        // macOS
        "/System/Library/Fonts/PingFang.ttc",
        "/System/Library/Fonts/Hiragino Sans GB.ttc",
    };

    private static readonly string[] LatinCandidates =
    {
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
        "/mnt/c/Windows/Fonts/segoeui.ttf",
        "/mnt/c/Windows/Fonts/arial.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
    };

    private readonly List<string> _searchPaths = new();

    public FontResolver(IEnumerable<string>? extraSearchPaths = null)
    {
        _searchPaths.AddRange(CjkCandidates);
        _searchPaths.AddRange(LatinCandidates);
        if (extraSearchPaths is not null)
            _searchPaths.InsertRange(0, extraSearchPaths.Where(Directory.Exists).SelectMany(SafeEnumerateFonts));
    }

    /// <summary>The font file chosen for the last render, if any.</summary>
    public string? LastResolved { get; private set; }

    /// <summary>Font files that were available, in resolution order.</summary>
    public IReadOnlyList<string> Candidates => _searchPaths.Where(File.Exists).ToList();

    /// <summary>
    /// Picks a font file. When <paramref name="requiresCjk"/> is true, prefers CJK-capable
    /// faces; otherwise returns the first available candidate.
    /// </summary>
    public string? Resolve(bool requiresCjk, string? preferredFontFile = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredFontFile))
        {
            if (File.Exists(preferredFontFile))
            {
                LastResolved = preferredFontFile;
                return preferredFontFile;
            }
        }

        IEnumerable<string> ordered = requiresCjk
            ? _searchPaths
            : _searchPaths.OrderBy(p => CjkCandidates.Contains(p) ? 1 : 0);

        foreach (var candidate in ordered)
        {
            if (!File.Exists(candidate)) continue;
            LastResolved = candidate;
            return candidate;
        }

        LastResolved = null;
        return null;
    }

    /// <summary>True when a CJK-capable font file is available on this machine.</summary>
    public static bool CjkFontAvailable() => CjkCandidates.Any(File.Exists);

    /// <summary>Heuristic: does this text contain non-Latin-1 characters that need CJK glyphs?</summary>
    public static bool RequiresCjk(IEnumerable<string?> texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (var ch in text)
            {
                if (ch < 0x0080) continue;
                if (ch is >= '\u2E80' and <= '\u9FFF') return true;
                if (ch is >= '\uAC00' and <= '\uD7AF') return true;
                if (ch is >= '\uF900' and <= '\uFAFF') return true;
                if (ch is >= '\uFF00' and <= '\uFFEF') return true;
            }
        }
        return false;
    }

    private static IEnumerable<string> SafeEnumerateFonts(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
