using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every code symbol named in `limitations.md` must exist in the source.
///
/// The file described hand-drawn geometry as preserved in `PreservedGeometrySections`. That type has never
/// existed — the geometry lives in `FreePath.Geometry`, with unknown row types kept verbatim and reported.
/// A reader following the old text would have searched for something that was not there and concluded the
/// capability was missing.
///
/// The claim was not wrong about the behaviour, only about where it lives — which is the kind of error that
/// survives review, because it reads correctly.
/// </summary>
public sealed class LimitationsSymbolTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Our own code: `src/` and `tests/`. Third-party names (OfficeIMO's own API) are excluded.</summary>
    private static string Source()
    {
        var roots = new[]
        {
            Path.Combine(Root(), "src"),
            Path.Combine(Root(), "tests")
        };

        return string.Join("\n", roots
            .SelectMany(r => Directory.GetFiles(r, "*.cs", SearchOption.AllDirectories))
            .Select(File.ReadAllText));
    }

    /// <summary>Names that belong to OfficeIMO.Visio, not to this project.</summary>
    private static readonly HashSet<string> ThirdParty = new(StringComparer.Ordinal)
    {
        "FixFile", "ValidateFile"
    };

    [Fact]
    public void EveryNamedTypeExistsInTheSource()
    {
        var doc = File.ReadAllText(Path.Combine(Root(), "docs", "limitations.md"));
        var source = Source();

        // Backticked PascalCase identifiers that look like types or members.
        var names = Regex.Matches(doc, "`([A-Z][A-Za-z0-9_]{3,})`")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(names);

        var missing = names
            .Where(n => !ThirdParty.Contains(n))
            .Where(n => !source.Contains(n, StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "named in limitations.md but absent from the source: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryWarningCodeExistsInTheSource()
    {
        var doc = File.ReadAllText(Path.Combine(Root(), "docs", "limitations.md"));
        var source = Source();

        var codes = Regex.Matches(doc, "`([a-z]+\\.[a-z-]+)`")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(codes);

        var missing = codes.Where(c => !source.Contains(c, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            "warning codes in limitations.md that the engine never emits: " + string.Join(", ", missing));
    }

    [Fact]
    public void L4StaysMarkedUnverified()
    {
        var doc = File.ReadAllText(Path.Combine(Root(), "docs", "limitations.md"));

        Assert.Contains("未执行", doc);
        Assert.Contains("不能替代", doc);
    }
}
