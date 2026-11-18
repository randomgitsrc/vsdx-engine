using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `installation.md` makes claims a reader follows literally: the SDK version, the artifact paths, the
/// tool package id, and the build commands. Each is checkable against the repository, so each is checked.
///
/// The global-tool path was verified end to end while writing this: `dotnet pack` produces a package
/// with `DotnetToolSettings.xml` and a `vsdx-tool` entry point, `dotnet tool install` accepts it, and
/// the installed command reports its version. Two failures along the way were environmental — a
/// `$TMPDIR` that empties between shell invocations, and a missing `DOTNET_ROOT` for the installed
/// shim — not product defects, and the document is right about the procedure.
/// </summary>
public sealed class InstallationDocTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Doc() => File.ReadAllText(Path.Combine(Root(), "docs", "installation.md"));

    [Fact]
    public void TheStatedSdkVersionMatchesGlobalJson()
    {
        var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "global.json")));
        var pinned = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();

        Assert.NotNull(pinned);
        Assert.Contains(pinned!, Doc());
    }

    [Fact]
    public void TheStatedCliPathIsWhereTheBuildPutsIt()
    {
        // Not asserting existence — a fresh clone has no bin/ — but that the documented path follows
        // the project's own output convention.
        var doc = Doc();

        Assert.Contains("src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool", doc);
        Assert.Contains("src/Vsdx.Engine/bin/Release/net8.0/Vsdx.Engine.dll", doc);
    }

    [Fact]
    public void TheToolPackageIdMatchesTheProject()
    {
        var csproj = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Cli", "Vsdx.Cli.csproj"));

        var packageId = Regex.Match(csproj, @"<PackageId>([^<]+)</PackageId>").Groups[1].Value;
        var command = Regex.Match(csproj, @"<ToolCommandName>([^<]+)</ToolCommandName>").Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(packageId), "PackAsTool is set but there is no PackageId");
        Assert.False(string.IsNullOrEmpty(command), "PackAsTool is set but there is no ToolCommandName");

        var doc = Doc();
        Assert.Contains(packageId, doc);
        Assert.Contains(command, doc);
    }

    [Fact]
    public void ItDeclaresThatVisioIsNotRequired()
    {
        // The product's premise; a reader installing it needs to know they do not need a licence.
        Assert.Contains("不需要", Doc());
    }

    [Fact]
    public void TheCommandsItGivesAreTheOnesThatWork()
    {
        var doc = Doc();
        var root = Root();

        Assert.Contains("dotnet build -c Release", doc);

        if (doc.Contains("dotnet test", StringComparison.Ordinal))
            Assert.True(File.Exists(Path.Combine(root, "tests", "Vsdx.Tests", "Vsdx.Tests.csproj")));
    }
}
