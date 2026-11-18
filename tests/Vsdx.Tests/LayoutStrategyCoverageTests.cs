using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every layout strategy must have at least one real example that runs and reports its score.
///
/// Of the six strategies, only four had examples — `swimlane` and `tree` had none, so neither had ever
/// been looked at. Adding them showed `tree` produces a clean three-level hierarchy (100, no issues) and
/// `swimlane` arranges lanes correctly by `order` but draws cross-lane edges as diagonals (88).
///
/// A strategy with no example is a strategy nobody has verified.
/// </summary>
public sealed class LayoutStrategyCoverageTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static (ExecutionResult Result, Diagram Written) Run(string irFile, string layout)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(irFile), Json)!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(
            $$"""{"specVersion":"1.0","theme":{"palette":"enterprise"},"layout":"{{layout}}","direction":"topToBottom"}""",
            Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-cov-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);
            return (result, new VsdxEngine().Read(path).Diagram);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SwimlaneHasARealExample()
    {
        var path = Path.Combine(Root(), "examples", "realworld", "order-swimlane.ir.json");
        Assert.True(File.Exists(path), "swimlane has no example diagram");

        var (result, written) = Run(path, "swimlane");

        Assert.True(result.Quality.HardConstraintsPassed);
        Assert.Equal(3, written.Pages[0].Groups.Count);
    }

    [Fact]
    public void TreeHasARealExample()
    {
        var path = Path.Combine(Root(), "examples", "realworld", "org-tree.ir.json");
        Assert.True(File.Exists(path), "tree has no example diagram");

        var (result, written) = Run(path, "tree");

        Assert.True(result.Quality.HardConstraintsPassed);
        Assert.Equal(100, result.Quality.Score);

        // A tree puts the root alone on the first row: it must be the highest node.
        var page = written.Pages[0];
        var highest = page.Nodes.OrderByDescending(n => n.Bounds!.Y).First();

        Assert.Equal("cto", highest.Id);
    }

    [Fact]
    public void EveryStrategyHasAnExample()
    {
        // The property worth holding: no strategy is shipped without something that exercises it.
        var examples = Path.Combine(Root(), "examples", "realworld");
        var all = string.Concat(Directory.GetFiles(examples, "*.ir.json")
            .Select(File.ReadAllText));

        Assert.True(all.Length > 0);

        // Strategies referenced by the example set, directly or by path name.
        var documented = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["layered"] = "cbo",
            ["flow"] = "user-data-flow",
            ["grid"] = "user-data-flow",
            ["manual"] = "checkout-sequence",
            ["swimlane"] = "order-swimlane",
            ["tree"] = "org-tree"
        };

        foreach (var (strategy, stem) in documented)
        {
            Assert.True(File.Exists(Path.Combine(examples, $"{stem}.ir.json")),
                $"{strategy} is expected to be exercised by {stem}.ir.json, which is missing");
        }
    }
}
