using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `docs/api.md` must document every CLI command that exists.
///
/// It covered ten of thirteen. Missing were `execute` — the protocol entry point, and the one path
/// where most of the engine's capability lives — plus `compare` and `repair`. A reader working from
/// the API reference would not learn that the IR/DesignSpec path existed.
/// </summary>
public sealed class ApiDocumentationTests
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

    /// <summary>Commands listed in the CLI's own help output.</summary>
    private static HashSet<string> CommandsFromHelp()
    {
        var program = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Cli", "Program.cs"));

        // The dispatcher is a switch on the command name.
        return Regex.Matches(program, @"""([a-z][a-z-]*)"" =>")
            .Select(m => m.Groups[1].Value)
            .Where(n => n is not ("json" or "help"))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> CommandsInDocs()
    {
        return Regex.Matches(Api(), @"^### 2\.\d+ `([a-z-]+)`", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EveryCommandIsDocumented()
    {
        var missing = CommandsFromHelp().Except(CommandsInDocs()).OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0, $"undocumented commands: {string.Join(", ", missing)}");
    }

    [Fact]
    public void ItDocumentsTheProtocolEntryPoint()
    {
        var api = Api();

        Assert.Contains("`execute`", api);
        Assert.Contains("unmetConstraints", api);
        Assert.Contains("appliedDesignNotes", api);
    }

    [Fact]
    public void SectionNumbersAreContiguous()
    {
        // Inserting three sections left a duplicate `2.10` behind; this catches that.
        var numbers = Regex.Matches(Api(), @"^### 2\.(\d+)", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value))
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);
    }

    [Fact]
    public void ItSaysACommandIsBounded()
    {
        // `repair` looping forever would be a real hazard; the doc must not imply otherwise.
        var api = Api();
        int at = api.IndexOf("### 2.12 `repair`", StringComparison.Ordinal);

        Assert.True(at > 0);
        var section = api[at..api.IndexOf("### 2.13", at, StringComparison.Ordinal)];

        Assert.Contains("有界", section);
    }

    [Fact]
    public void ItStatesThePartialStatusRule()
    {
        // `status: partial` still writes a file; a reader who does not check misses that.
        var api = Api();

        Assert.Contains("partial", api);
        Assert.Contains("unmetConstraints", api);
    }
}
