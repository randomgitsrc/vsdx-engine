using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `installation.md` has to describe the tool that exists and the paths that resolve.
///
/// Walked the whole install path by hand: `dotnet restore`, `pack`, `tool install --global`, run,
/// `tool uninstall` — all fine, and the version reads 0.9.0 from the assembly rather than a literal.
///
/// **The instructions had a gap that only shows up when you follow them.** They end with
/// `vsdx-tool version --json` and no note about PATH; the binary lands under `$DOTNET_CLI_HOME/.dotnet/tools`,
/// which is not `~/.dotnet/tools` when that variable is set — as it is here. Following the page exactly
/// produced "command not found". Now documented, with `dotnet tool list --global` to confirm the install
/// and the variable-expansion that finds the right directory.
/// </summary>
public sealed class InstallationClaimsTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Doc() =>
        File.ReadAllText(Path.Combine(Root(), "docs", "installation.md"));

    [Fact]
    public void ThePackageIdMatchesTheProject()
    {
        // The documented install command must name the id the project actually produces.
        var csproj = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Cli", "Vsdx.Cli.csproj"));

        var id = Regex.Match(csproj, "<PackageId>([^<]+)</PackageId>").Groups[1].Value;

        Assert.False(string.IsNullOrWhiteSpace(id), "Vsdx.Cli.csproj declares no PackageId");
        Assert.Contains(id, Doc());
    }

    [Fact]
    public void TheToolCommandNameIsDocumented()
    {
        var csproj = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Cli", "Vsdx.Cli.csproj"));

        var command = Regex.Match(csproj, "<ToolCommandName>([^<]+)</ToolCommandName>").Groups[1].Value;

        Assert.False(string.IsNullOrWhiteSpace(command), "no ToolCommandName — the global tool would have no name");
        Assert.Contains(command, Doc());
    }

    [Fact]
    public void ThePathCaveatIsPresent()
    {
        // Without it the page ends on a command that fails on any machine with DOTNET_CLI_HOME set.
        var doc = Doc();

        Assert.Contains("DOTNET_CLI_HOME", doc);
        Assert.Contains("dotnet tool list --global", doc);
    }

    [Fact]
    public void EveryFontPathItListsIsAbsolute()
    {
        // A relative "suggestion" would be useless: the resolver probes absolute locations.
        var paths = Regex.Matches(Doc(), @"`(/[^`]+\.(?:ttf|ttc)|[A-Z]:\\[^`]+\.(?:ttf|ttc))`")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.True(p.StartsWith('/') || Regex.IsMatch(p, @"^[A-Z]:")));
    }
}
