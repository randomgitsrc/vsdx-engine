using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Guards the shape of the suite, not the product: a large number of tests proves nothing if they all
/// exercise one path or duplicate each other.
///
/// Measured on 150 files and 878 methods:
///
///   LayoutAnalyzer.Analyze        107 calls / 31 files
///   DiagramLayoutEngine.Apply      38 calls / 14 files
///   DiagramLayout.Apply            12 calls /  8 files
///   DiagramSizer.Apply             12 calls /  5 files
///   OrthogonalRouter.Route         13 calls /  4 files
///   engine entry points           Read 234, Create 131, Execute 78, Update 65, Validate 39, Render 22
///
/// The busiest eight files hold 15% of the cases, so nothing is concentrated. Two method names repeat
/// across different classes, which is permitted.
///
/// **My first pass at this reported the pipeline as barely covered** — one call each to the layout, sizer
/// and router. I had counted matching *files* with `grep -c` and read the result as call counts. The
/// coverage was fine; the measurement was not.
/// </summary>
public sealed class TestSuiteShapeTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string[] TestFiles() =>
        Directory.GetFiles(Path.Combine(Root(), "tests", "Vsdx.Tests"), "*.cs");

    private static int CallsTo(string symbol) =>
        TestFiles().Sum(f => Regex.Matches(File.ReadAllText(f), Regex.Escape(symbol)).Count);

    [Fact]
    public void TheLayoutPipelineIsExercisedDirectly()
    {
        // Not only through `VsdxEngine.Create`: each stage has to be reachable on its own, or a defect in
        // one stage can hide behind a passing integration test.
        Assert.True(CallsTo("LayoutAnalyzer.Analyze") >= 20, "analysis is only reached through the facade");
        Assert.True(CallsTo("DiagramLayoutEngine.Apply") >= 10, "layout strategies are not called directly");
        Assert.True(CallsTo("DiagramLayout.Apply") >= 5, "container derivation is not called directly");
    }

    [Fact]
    public void EveryEngineEntryPointIsCovered()
    {
        foreach (var method in new[] { "Read", "Create", "Update", "Render", "Validate" })
            Assert.True(CallsTo("." + method + "(") >= 5, $"`{method}` is barely exercised");
    }

    [Fact]
    public void NoSingleFileHoldsTooMuchOfTheSuite()
    {
        var counts = TestFiles()
            .Select(f => Regex.Matches(File.ReadAllText(f), @"public (?:void|async Task) \w+\(").Count)
            .Where(n => n > 0)
            .ToList();

        int total = counts.Sum();

        Assert.True(total >= 500, $"only {total} test methods found — is the path right?");
        Assert.True(counts.Max() < total * 0.10,
            $"one file holds {counts.Max()} of {total} cases");
    }

    [Fact]
    public void DuplicateMethodNamesLiveInDifferentClasses()
    {
        // A repeated name inside one class would mean the later one silently replaces the earlier.
        foreach (var file in TestFiles())
        {
            var names = Regex.Matches(File.ReadAllText(file), @"public (?:void|async Task) (\w+)\(")
                .Select(m => m.Groups[1].Value)
                .ToList();

            var duplicated = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

            Assert.True(duplicated.Count == 0,
                $"{Path.GetFileName(file)} declares {string.Join(", ", duplicated)} more than once");
        }
    }
}
