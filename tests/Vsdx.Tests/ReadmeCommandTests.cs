using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every path and command the README shows must resolve.
///
/// The README is the first thing a new reader runs, so a wrong path costs them before they have any way to
/// tell whose fault it is. Walked the whole quickstart by hand — `create`, `render`, `validate`,
/// `check-layout` — plus `inspect`, `diff`, `execute` and all three `batch` forms; every one worked and
/// every referenced file existed.
///
/// What this test adds is the cheap half of that: the file paths it names, checked on every run. Running the
/// commands themselves needs a built CLI and a writable output directory, which the suite cannot assume.
/// </summary>
public sealed class ReadmeCommandTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Readme() => File.ReadAllText(Path.Combine(Root(), "README.md"));

    [Fact]
    public void EveryRepoPathTheReadmeNamesExists()
    {
        var root = Root();
        var missing = new List<string>();

        // Bare repo-relative paths inside code blocks or backticks, with a known top-level directory.
        foreach (Match m in Regex.Matches(Readme(), @"(?:^|[\s`(--])((?:src|tests|docs|demo|examples|skill|tools|benchmarks)/[A-Za-z0-9._/-]+)"))
        {
            var path = m.Groups[1].Value.TrimEnd('.', ',', ')');

            if (path.EndsWith('/')) continue;                       // a directory, checked below
            if (path.Contains('<') || path.Contains('*')) continue;  // a placeholder or a glob

            if (!File.Exists(Path.Combine(root, path)) && !Directory.Exists(Path.Combine(root, path)))
                missing.Add(path);
        }

        Assert.True(missing.Count == 0, "named in README but absent: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryLinkedDocumentExists()
    {
        var root = Root();
        var readme = Readme();

        var links = Regex.Matches(readme, @"\]\(([^)]+)\)")
            .Select(m => m.Groups[1].Value.Split('#')[0])
            .Where(l => l.Length > 0 && !l.StartsWith("http", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(links);

        // A link may point at a directory; both resolve.
        var missing = links
            .Where(l => !File.Exists(Path.Combine(root, l)) && !Directory.Exists(Path.Combine(root, l)))
            .ToList();

        Assert.True(missing.Count == 0, "broken links in README: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheReadmeNamesNoCommandTheCliDoesNotHave()
    {
        var cli = File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Cli", "Program.cs"));
        var readme = Readme();

        var verbs = Regex.Matches(readme, @"vsdx-tool ([a-z][a-z-]*)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(verbs);

        var missing = verbs.Where(v => !cli.Contains("\"" + v + "\"", StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            "README shows commands the CLI does not dispatch: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheQuickstartKeepsItsFourSteps()
    {
        // create → render → validate → check-layout. A quickstart that drops a step teaches a habit that
        // leaves the deliverable unchecked.
        var readme = Readme();

        foreach (var step in new[] { "create", "render", "validate", "check-layout" })
            Assert.Contains(step, readme);
    }
}
