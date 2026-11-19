using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `L` means one thing in this project: a verification level (L1 package / L2 read-back / L3 render /
/// L4 native Visio). Backend restrictions use `R`.
///
/// They used to share the prefix. `limitations.md` numbered its OfficeIMO limits L1–L8 while the rest of the
/// project used L1–L4 for verification levels, so in that one file `L4` was both "`VisioConnector.From/To`
/// is `internal set`" and "native Visio verification has never run" — two unrelated facts behind one label,
/// twenty lines apart.
///
/// Renamed `R1`–`R8`, with a note in the section saying why, and the four cross-references updated.
/// </summary>
public sealed class NumberingSchemeTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> Docs() =>
        Directory.GetFiles(Path.Combine(Root(), "docs"), "*.md", SearchOption.AllDirectories)
            .Concat(new[] { Path.Combine(Root(), "PROJECT-STATUS.md"), Path.Combine(Root(), "README.md") })
            .Where(File.Exists);

    [Fact]
    public void BackendRestrictionsAreNumberedR()
    {
        var limitations = File.ReadAllText(Path.Combine(Root(), "docs", "limitations.md"));

        var restrictions = Regex.Matches(limitations, @"^\| R(\d+) \|", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();

        Assert.NotEmpty(restrictions);
        Assert.Equal(Enumerable.Range(1, restrictions.Count), restrictions.OrderBy(n => n));
    }

    [Fact]
    public void NoDocumentCitesAnLNumberedBackendRestriction()
    {
        // `（L7）` and `限制 L7` were the two forms in use. The verification levels are cited as
        // "L1–L4" or "L4", never as a parenthesised eight-level reference.
        var offenders = new List<string>();

        foreach (var file in Docs())
        {
            var text = File.ReadAllText(file);

            foreach (Match m in Regex.Matches(text, @"(?:（|限制\s*)L([5-8])"))
                offenders.Add($"{Path.GetFileName(file)}: L{m.Groups[1].Value}");
        }

        Assert.True(offenders.Count == 0,
            "backend restrictions still cited with an L number: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheVerificationLevelsKeepTheirLNames()
    {
        // The rename must not have touched L1–L4 as verification levels.
        var limitations = File.ReadAllText(Path.Combine(Root(), "docs", "limitations.md"));

        Assert.Contains("L1 包结构", limitations);
        Assert.Contains("L4 Visio 原生", limitations);
    }

    [Fact]
    public void TheSectionExplainsWhyThePrefixDiffers()
    {
        var limitations = File.ReadAllText(Path.Combine(Root(), "docs", "limitations.md"));

        Assert.Contains("Restriction", limitations);
        Assert.Contains("区分开", limitations);
    }
}
