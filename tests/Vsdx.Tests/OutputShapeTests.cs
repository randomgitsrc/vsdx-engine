using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The CLI does not have one JSON shape — it has three, and a caller has to know which command it is reading.
///
///   `execute`           ExecutionResult: `warnings[]` with a `severity` on each entry
///   `create` / `update` write result:    `compatibility.issues[]`
///   `repair`            process record:  `attempts[]`, `baseline`, `final` — no issue list at all
///
/// That is defensible: the three answer different questions. What was not defensible is that `api.md` showed
/// only the `compatibility` form and never mentioned `warnings`, so a reader building a generic consumer would
/// look for a field that does not exist on half the commands and conclude there were no problems.
///
/// It cost me a detour in round 103: I read `warnings` from `create`, got an empty list, and briefly concluded
/// a fix had not worked.
/// </summary>
public sealed class OutputShapeTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Api() => File.ReadAllText(Path.Combine(Root(), "docs", "api.md"));

    [Fact]
    public void TheApiDocumentDescribesBothIssueLocations()
    {
        var api = Api();

        Assert.Contains("compatibility", api);
        Assert.Contains("`warnings`", api);
        Assert.Contains("severity", api);
    }

    [Fact]
    public void ItWarnsThatTheShapeVariesByCommand()
    {
        // The sentence that stops a generic consumer being written against one shape.
        var api = Api();

        Assert.Contains("两种输出格式", api);
        Assert.Contains("不要混用", api);
    }

    [Fact]
    public void ItNamesTheCommandThatHasNoWarningsField()
    {
        // `create --json` is the specific trap.
        var api = Api();

        Assert.Contains("create --json", api);
    }

    [Fact]
    public void TheExecutionResultFieldsAreAllListed()
    {
        // Whatever the engine puts in an ExecutionResult should be documented, not just the interesting ones.
        var api = Api();

        foreach (var field in new[]
                 {
                     "status", "appliedDesignNotes", "unmetConstraints", "stagesCompleted", "warnings",
                     "errors", "objectMapping"
                 })
            Assert.Contains(field, api);
    }

    [Fact]
    public void EveryCommandThatEmitsJsonIsDocumentedWithItsShape()
    {
        // A command with `--json` but no documented shape is a command a consumer has to guess at.
        var api = Api();
        var program = File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Cli", "Program.cs"));

        var commands = Regex.Matches(program, @"case ""([a-z][a-z-]+)"":")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var missing = commands.Where(c => !api.Contains(c, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0, "commands with no entry in api.md: " + string.Join(", ", missing));
    }
}
