using System.Text.Json;
using System.Text.RegularExpressions;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The model example in `skill/SKILL.md` must produce a deliverable diagram.
///
/// It did not. The three nodes were pinned at x=5.50 on an 18×11 page, so the content occupied 9% of the
/// page — `page.underused`, a hard failure, score 0/100. `--fit-page` made it worse (9%). A reader copying
/// the documented "minimal but complete example" as their first step got a zero.
///
/// The example now uses a 6×9 page with the nodes on its centre line: score 98, one informational margin
/// note and no violations.
///
/// `SkillDocumentationTests` already checked that the commands it names exist. What was missing was running
/// the example at all.
/// </summary>
public sealed class SkillExampleTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>The JSON block under "## 3. Write the model".</summary>
    private static string ExampleJson()
    {
        var doc = File.ReadAllText(Path.Combine(Root(), "skill", "SKILL.md"));
        var start = doc.IndexOf("## 3. Write the model", StringComparison.Ordinal);

        Assert.True(start > 0, "the model-writing section is missing");

        var match = Regex.Match(doc[start..], @"```json\n(.*?)```", RegexOptions.Singleline);

        Assert.True(match.Success, "that section has no json block");

        return match.Groups[1].Value;
    }

    /// <summary>Writes the documented example to a temp file and loads it, the way a reader would.</summary>
    private static (Diagram Model, string File) Build()
    {
        var file = Path.Combine(Path.GetTempPath(), $"vsdx-skill-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, ExampleJson());

        return (DiagramJson.Load(file), file);
    }

    [Fact]
    public void TheExampleParses()
    {
        var (model, file) = Build();

        try
        {
            Assert.Equal("order-platform", model.Id);
            Assert.Single(model.Pages);
            Assert.Equal(3, model.Pages[0].Nodes.Count);
            Assert.Equal(2, model.Pages[0].Edges.Count);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TheExamplePassesEveryHardConstraint()
    {
        var (_, json) = Build();
        var vsdx = Path.Combine(Path.GetTempPath(), $"vsdx-skill-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(json, vsdx, new WriteOptions { AutoLayout = false });

            var report = LayoutAnalyzer.Analyze(new VsdxEngine().Read(vsdx).Diagram);

            Assert.True(report.HardConstraintsPassed,
                "the documented example trips a hard constraint: "
                + string.Join(", ", report.Errors.Select(e => e.Code)));
        }
        finally
        {
            File.Delete(json);
            if (File.Exists(vsdx)) File.Delete(vsdx);
        }
    }

    [Fact]
    public void TheExampleDoesNotTripPageUnderused()
    {
        // The specific failure: content pinned to one column of an 18x11 page, 9% used, score 0.
        //
        // Measured the way the analyser measures it — over every visible object, container included. The
        // nodes alone occupy 27%, the container brings it to 38%; only the second number decides whether
        // `page.underused` fires, and using the first made this test report a failure that was not there.
        var (_, json) = Build();
        var vsdx = Path.Combine(Path.GetTempPath(), $"vsdx-skill-u-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(json, vsdx, new WriteOptions { AutoLayout = false });

            var report = LayoutAnalyzer.Analyze(new VsdxEngine().Read(vsdx).Diagram);

            Assert.DoesNotContain(report.Issues, i => i.Code == "page.underused");
        }
        finally
        {
            File.Delete(json);
            if (File.Exists(vsdx)) File.Delete(vsdx);
        }
    }

    [Fact]
    public void TheExampleKeepsItsTeachingPoints()
    {
        // It exists to show containers, shape data, two edge labels and mixed shapes; shrinking the page
        // must not have dropped any of them.
        var (model, file) = Build();
        var page = model.Pages[0];
        File.Delete(file);

        Assert.Single(page.Groups);
        Assert.Contains(page.Nodes, n => n.ShapeData is { Count: 2 });
        Assert.Contains(page.Nodes, n => n.Text!.Contains("Kong", StringComparison.Ordinal));
        Assert.All(page.Edges, e => Assert.False(string.IsNullOrEmpty(e.Label)));
    }
}
