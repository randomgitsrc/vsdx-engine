using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A `repair` attempt reports eight fields, and all eight have to mean something.
///
/// Checked against a real repair: `problem`, `rootCause`, `strategy`, `expectedGain`, `actualGain`, `decision`,
/// `introducedNewIssues`, `note`. The accepted run's `actualGain` was verified against a fresh `check-layout` of
/// the output — 95 points and the target issue gone — so it is a measurement, not a restatement.
///
/// **One place the numbers did not match**: `actualGain` said "问题数 3 → 1" while `check-layout` reported four
/// issues on the same input. Both were right — the repair count is errors plus warnings, and the fourth was an
/// `info` note. The wording now says which count it is, because a reader comparing the two numbers has no way
/// to know they are measuring different things.
/// </summary>
public sealed class RepairAttemptFieldsTests
{
    private static Diagram Broken()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 4, Height = 3 } };

        // Two nodes outside a 4x3 page, plus the margins and utilisation notes that follow from it.
        page.Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(5, 4, 1.6, 0.7) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Bounds = new Bounds(7, 4, 1.6, 0.7) });
        page.Edges.Add(new Edge { Id = "e", From = "a", To = "b" });

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void TheRepairCountIsErrorsAndWarningsOnly()
    {
        var vsdx = Path.Combine(Path.GetTempPath(), $"vsdx-cnt-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(Broken(), vsdx, new WriteOptions { AutoLayout = false });

            var report = LayoutAnalyzer.Analyze(new VsdxEngine().Read(vsdx).Diagram);

            var counted = report.Errors.Count() + report.Warnings.Count();
            var all = report.Issues.Count;

            Assert.True(all > counted, "this fixture is meant to include an info note");
            Assert.Equal(counted + 1, all);
        }
        finally { if (File.Exists(vsdx)) File.Delete(vsdx); }
    }

    [Fact]
    public void ADocumentedCountSaysWhichCountItIs()
    {
        // The repair message must not present a number that disagrees with `check-layout` without saying why.
        var source = File.ReadAllText(
            Path.Combine(RootOf(), "src", "Vsdx.Engine", "RepairController.cs"));

        Assert.Contains("error+warning", source);
    }

    [Fact]
    public void TheAttemptOffersAPlanForARealFailure()
    {
        var vsdx = Path.Combine(Path.GetTempPath(), $"vsdx-plan-{Guid.NewGuid():N}.vsdx");

        try
        {
            var engine = new VsdxEngine();
            engine.Create(Broken(), vsdx, new WriteOptions { AutoLayout = false });

            var controller = new RepairController(2, 6);
            controller.Begin(engine.Read(vsdx).Diagram, "baseline");

            var plan = controller.NextPlan(engine.Read(vsdx).Diagram);

            Assert.NotNull(plan);
            Assert.False(string.IsNullOrWhiteSpace(plan!.IssueCode));
            Assert.False(string.IsNullOrWhiteSpace(plan.RecommendedFix));
        }
        finally { if (File.Exists(vsdx)) File.Delete(vsdx); }
    }

    private static string RootOf()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
