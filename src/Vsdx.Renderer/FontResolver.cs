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

    /// <summary>
    /// Reads the family name from a TrueType/OpenType file's `name` table.
    ///
    /// The `name` table is a public binary format (a tag, an offset, a length, then string records),
    /// so this needs no font library and no external process. Returns null for anything that is not
    /// a parseable font rather than throwing: a directory scan will meet files that are not fonts.
    /// </summary>
    public static string? ReadFamilyName(string path)
    {
        byte[] data;
        try
        {
            if (!File.Exists(path)) return null;
            data = File.ReadAllBytes(path);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        if (data.Length < 12) return null;

        int numTables = (data[4] << 8) | data[5];
        if (numTables <= 0) return null;

        int nameOffset = -1;
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + i * 16;
            if (record + 16 > data.Length) break;

            if (data[record] != 'n' || data[record + 1] != 'a'
                || data[record + 2] != 'm' || data[record + 3] != 'e') continue;

            nameOffset = (data[record + 8] << 24) | (data[record + 9] << 16)
                       | (data[record + 10] << 8) | data[record + 11];
            break;
        }

        if (nameOffset < 0 || nameOffset + 6 > data.Length) return null;

        int count = (data[nameOffset + 2] << 8) | data[nameOffset + 3];
        int stringOffset = (data[nameOffset + 4] << 8) | data[nameOffset + 5];

        for (int i = 0; i < count; i++)
        {
            int record = nameOffset + 6 + i * 12;
            if (record + 12 > data.Length) break;

            int platformId = (data[record] << 8) | data[record + 1];
            int nameId = (data[record + 6] << 8) | data[record + 7];
            int length = (data[record + 8] << 8) | data[record + 9];
            int offset = (data[record + 10] << 8) | data[record + 11];

            // nameId 1 is the family. Platform 3 is Windows, which stores UTF-16BE.
            if (nameId != 1 || platformId != 3) continue;

            int start = nameOffset + stringOffset + offset;
            if (start + length > data.Length) continue;

            return System.Text.Encoding.BigEndianUnicode.GetString(data, start, length);
        }

        return null;
    }

    /// <summary>
    /// Finds an installed font file whose family matches <paramref name="family"/>, or null.
    ///
    /// Null rather than a substitute: quietly drawing in some other face would leave the caller
    /// believing their font was used, which is the failure this method exists to end.
    /// </summary>
    public string? ResolveByFamily(string? family, bool requiresCjk)
    {
        if (string.IsNullOrWhiteSpace(family)) return null;

        foreach (var candidate in EnumerateFontFiles(requiresCjk))
        {
            var name = ReadFamilyName(candidate);
            if (name is null) continue;
            if (!string.Equals(name.Trim(), family.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

            LastResolved = candidate;
            return candidate;
        }

        return null;
    }

    private IEnumerable<string> EnumerateFontFiles(bool requiresCjk)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in _searchPaths)
            if (File.Exists(candidate) && seen.Add(candidate)) yield return candidate;

        foreach (var dir in FontDirectories)
        {
            if (!Directory.Exists(dir)) continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                 && !ext.Equals(".otf", StringComparison.OrdinalIgnoreCase)) continue;

                if (seen.Add(file)) yield return file;
            }
        }
    }

    private static readonly string[] FontDirectories =
    {
        "/usr/share/fonts",
        "/usr/local/share/fonts",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/fonts"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts")
    };
}
