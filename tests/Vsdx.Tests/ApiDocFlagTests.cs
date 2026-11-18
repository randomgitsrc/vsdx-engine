using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every flag the CLI accepts must be in `api.md`, and every flag in `api.md` must be accepted.
///
/// Five were missing from the document — `--layout`, `--fit-text`, `--fit-page`, `--recenter` and
/// `--no-recalc` — and they are the ones a caller most needs: without the three `fit`/`recenter` flags a
/// model with hand-written coordinates lands as a small cluster on a large page and fails
/// `page.underused`. That is exactly what the skill example did before round 80.
///
/// Reading the source for this is fiddly: options are looked up by bare name (`options.Require("input")`,
/// `options.Get("format")`), never as `"--input"`. My first extraction searched for the quoted `--` form and
/// concluded thirty of thirty-one documented flags did not exist.
/// </summary>
public sealed class ApiDocFlagTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Flag names the CLI reads, in the bare form the option reader uses.</summary>
    private static HashSet<string> SourceFlags()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Cli", "Program.cs"));

        return Regex.Matches(source, @"options\.\w+\(""([a-z][a-z-]+)""")
            .Select(m => m.Groups[1].Value)
            .Where(f => f.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> DocumentedFlags()
    {
        var doc = File.ReadAllText(Path.Combine(Root(), "docs", "api.md"));

        return Regex.Matches(doc, @"--([a-z][a-z-]+)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EveryDocumentedFlagIsAccepted()
    {
        var missing = DocumentedFlags().Except(SourceFlags()).OrderBy(f => f).ToList();

        Assert.True(missing.Count == 0,
            "documented but not read by the CLI: " + string.Join(", ", missing.Select(f => "--" + f)));
    }

    [Fact]
    public void EveryAcceptedFlagIsDocumented()
    {
        // The direction that actually failed: five layout flags existed and were undocumented.
        var undocumented = SourceFlags().Except(DocumentedFlags()).OrderBy(f => f).ToList();

        Assert.True(undocumented.Count == 0,
            "accepted by the CLI but absent from api.md: " + string.Join(", ", undocumented.Select(f => "--" + f)));
    }

    [Fact]
    public void TheLayoutFlagsAreGroupedAndExplained()
    {
        // A flag list without a sentence about when to use it is still a gap.
        var doc = File.ReadAllText(Path.Combine(Root(), "docs", "api.md"));

        foreach (var flag in new[] { "--layout", "--fit-text", "--fit-page", "--recenter" })
            Assert.Contains(flag, doc);

        Assert.Contains("page.underused", doc);
    }

    [Fact]
    public void EveryCommandInTheHelpIsDocumented()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Cli", "Program.cs"));
        var doc = File.ReadAllText(Path.Combine(Root(), "docs", "api.md"));

        var commands = Regex.Matches(source, @"case ""([a-z][a-z-]+)"":")
            .Select(m => m.Groups[1].Value)
            .Where(c => c != "help")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(commands);

        var missing = commands.Where(c => !doc.Contains(c, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0, "commands absent from api.md: " + string.Join(", ", missing));
    }
}
