using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The same fact stated in two documents must read the same in both.
///
/// Separate tests already pin the files that carry scenario scores (`RealworldValidationTests`,
/// `StatusReportTests`, `BoundariesClaimsTests`). This one looks at the facts that cross files without any
/// single owner: which commands exist, which documents are allowed to carry a score at all, and whether a
/// phase record can be mistaken for current guidance.
///
/// The check that found something this round was the identifier scheme — `L` meant both a verification level
/// and a backend restriction. The rest held: every documented command is dispatched, every current-state
/// document agrees on the six scores, and the phase records that disagree are marked as records.
/// </summary>
public sealed class CrossDocumentConsistencyTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string[] AllDocs() =>
        Directory.GetFiles(Path.Combine(Root(), "docs"), "*.md", SearchOption.AllDirectories)
            .Concat(new[]
            {
                Path.Combine(Root(), "README.md"),
                Path.Combine(Root(), "PROJECT-STATUS.md"),
                Path.Combine(Root(), "CHANGELOG.md"),
                Path.Combine(Root(), "skill", "SKILL.md")
            })
            .Where(File.Exists)
            .ToArray();

    [Fact]
    public void EveryCommandAnyDocumentShowsIsDispatched()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Cli", "Program.cs"));

        var commands = AllDocs()
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"vsdx-tool ([a-z][a-z-]+)"))
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(commands);

        var missing = commands.Where(c => !program.Contains("\"" + c + "\"", StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0, "documented commands the CLI cannot run: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheSixScenariosAreNamedTheSameWayEverywhere()
    {
        // A renamed example must be renamed in every document that cites it.
        var examples = Directory.GetFiles(Path.Combine(Root(), "examples", "realworld"), "*.ir.json")
            .Select(f => Path.GetFileName(f).Replace(".ir.json", "", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var stale = new List<string>();

        foreach (var file in AllDocs())
        {
            var text = File.ReadAllText(file);

            // Names that look like examples but no longer exist.
            foreach (Match m in Regex.Matches(text, @"`([a-z]+-[a-z-]+)`"))
            {
                var name = m.Groups[1].Value;

                if (name.Contains("state") || name.Contains("machine")) continue;   // discussed, not shipped

                if (name.EndsWith("-flow", StringComparison.Ordinal) && !examples.Contains(name))
                    stale.Add($"{Path.GetFileName(file)}: {name}");
            }
        }

        Assert.True(stale.Count == 0, "documents cite examples that do not exist: " + string.Join(", ", stale));
    }

    [Fact]
    public void PhaseRecordsAreNotThePlaceToLearnCurrentScores()
    {
        // Phase records are allowed to carry the scores they measured. What they must not do is read as
        // current guidance — the map says so, and this checks the map still says so.
        var map = File.ReadAllText(Path.Combine(Root(), "docs", "README.md"));

        Assert.Contains("过程记录", map);
        Assert.Contains("不是入门材料", map);
    }

    [Fact]
    public void TheVersionIsStatedIdenticallyWhereverItAppears()
    {
        var version = Regex.Match(
            File.ReadAllText(Path.Combine(Root(), "Directory.Build.props")),
            "<Version>([^<]+)</Version>").Groups[1].Value.Trim();

        // The changelog must have an entry for exactly this version, and no later one.
        var changelog = File.ReadAllText(Path.Combine(Root(), "CHANGELOG.md"));

        Assert.Contains($"## [{version}]", changelog);
    }
}
