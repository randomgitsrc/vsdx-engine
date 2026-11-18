using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The changelog's claims must match the code it describes.
///
/// Release notes are the one document a user reads before deciding to adopt a version, so a number
/// that has drifted is worse than no number. Everything asserted below was counted from the source
/// before being written, not recalled.
/// </summary>
public sealed class ChangelogTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Changelog() => File.ReadAllText(Path.Combine(Root(), "CHANGELOG.md"));

    private static string Analyzer() =>
        File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Engine", "LayoutAnalyzer.cs"));

    private static int DistinctIssueCodes() =>
        Regex.Matches(Analyzer(), @"Code = ""([a-z.\-]+)""")
            .Select(m => m.Groups[1].Value).Distinct().Count();

    private static int ErrorIssueCodes() =>
        Regex.Matches(Analyzer(), @"Code = ""([a-z.\-]+)""[^}]*?Severity = ""error""", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value).Distinct().Count();

    [Fact]
    public void TheStatedIssueCountMatches()
    {
        int actual = DistinctIssueCodes();

        Assert.Contains($"{actual} 类问题", Changelog());
    }

    [Fact]
    public void TheStatedErrorCountMatches()
    {
        int actual = ErrorIssueCodes();

        Assert.Contains($"{actual} 类为 error 级", Changelog());
    }

    [Fact]
    public void TheStatedTestCountIsPlausible()
    {
        // The exact number changes with every commit, so this checks the claim is stated at all and
        // is in a believable range rather than pinning a value that will rot.
        var match = Regex.Match(Changelog(), @"(\d+) 项通过");

        Assert.True(match.Success, "the changelog does not state a test count");
        Assert.InRange(int.Parse(match.Groups[1].Value), 500, 5000);
    }

    [Fact]
    public void ItSaysTheVersionIsNotReleasedYet()
    {
        var changelog = Changelog();

        Assert.Contains("未发布", changelog);
    }

    [Fact]
    public void TheVersionComesFromTheAssembly()
    {
        // The version used to be declared twice — Directory.Build.props and EngineInfo.Version —
        // and only one moved when it changed, so `vsdx-tool version` reported a number the build did
        // not use. EngineInfo now reads the assembly.
        var declared = Regex.Match(
            File.ReadAllText(Path.Combine(Root(), "Directory.Build.props")),
            @"<Version>([^<]+)</Version>").Groups[1].Value;

        Assert.Equal(declared, Vsdx.Adapters.EngineInfo.Version);
    }

    [Fact]
    public void TheVersionIsBelowOnePointOh()
    {
        // Pinned deliberately: 1.0.0 would claim the core promise is verified, and L4 has not run.
        // Raising this without running L4 is a decision, not a mechanical bump.
        var declared = Regex.Match(
            File.ReadAllText(Path.Combine(Root(), "Directory.Build.props")),
            @"<Version>([^<]+)</Version>").Groups[1].Value;

        Assert.StartsWith("0.", declared);
    }

    [Fact]
    public void ItKeepsDeclaringThatL4HasNotRun()
    {
        // The one claim that must never be softened between versions.
        Assert.Contains("L4", Changelog());
        Assert.Contains("从未执行", Changelog());
    }

    [Fact]
    public void EveryPaletteItNamesExists()
    {
        var changelog = Changelog();

        foreach (var name in new[] { "enterprise", "muted", "technical", "midnight" })
        {
            if (!changelog.Contains(name, StringComparison.Ordinal)) continue;

            Assert.True(Vsdx.Model.Palettes.TryGet(name, out _),
                $"the changelog names palette '{name}', which does not exist");
        }
    }
}
