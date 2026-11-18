using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `page.underused` reads the same utilisation figure as `quality.pageUtilisation`, so when that figure is
/// inflated by dispersed decorations the check silently stops firing.
///
/// Measured on two of the six reference diagrams:
///
///   user-data-flow       reported 82%, nodes alone 24%
///   checkout-sequence    reported 77%, nodes alone 23%
///
/// Both are below the 30% threshold on the drawing itself and neither reports `page.underused`. The two
/// that do not miss — `deploy-topology` and `org-tree` — have their text near the nodes, so the two
/// measurements agree.
///
/// This is the consequence of the measurement issue recorded in `known-issues.md` §7: not a wrong verdict
/// on a page, but a check that never reaches its verdict.
/// </summary>
public sealed class UnderusedMissTests
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

    private static (double Reported, double NodesOnly, bool UnderusedReported) Measure(string scenario, string specJson)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(Path.Combine(Root(), "examples", "realworld", $"{scenario}.ir.json")), Json)!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-um-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);
            var page = new VsdxEngine().Read(path).Diagram.Pages[0];

            double w = page.Size!.Width, h = page.Size.Height;

            var nodes = page.Nodes;
            double x0 = nodes.Min(n => n.Bounds!.X - n.Bounds!.W / 2);
            double x1 = nodes.Max(n => n.Bounds!.X + n.Bounds!.W / 2);
            double y0 = nodes.Min(n => n.Bounds!.Y - n.Bounds!.H / 2);
            double y1 = nodes.Max(n => n.Bounds!.Y + n.Bounds!.H / 2);

            return (
                result.Quality.PageUtilisation.Values.Single(),
                (x1 - x0) * (y1 - y0) / (w * h),
                result.Quality.IssueCodes.Contains("page.underused"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("user-data-flow")]
    [InlineData("checkout-sequence")]
    public void ASparseDrawingIsNotReportedWhenDecorationsInflateTheFigure(string scenario)
    {
        var (reported, nodesOnly, underused) = Measure(scenario,
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");

        // The premise: the drawing really is sparse.
        Assert.True(nodesOnly < 0.30, $"{scenario}: nodes occupy {nodesOnly:P0}, so this fixture stopped being sparse");

        // The defect: the headline figure hides it, and the check therefore never fires.
        Assert.True(reported > 0.30, $"{scenario}: reported {reported:P0}");
        Assert.False(underused, $"{scenario}: page.underused fired — the miss has been fixed, update this test");
    }

    [Fact]
    public void APageWhoseNodesFillItIsReportedAsFine()
    {
        // The other side: a genuinely full page must not be flagged, whatever the text does.
        var (reported, nodesOnly, underused) = Measure("org-tree",
            """{"specVersion":"1.0","layout":"tree","direction":"topToBottom"}""");

        Assert.True(nodesOnly > 0.5);
        Assert.True(reported > 0.5);
        Assert.False(underused);
    }

    [Fact]
    public void TheGapIsWideEnoughToMatter()
    {
        // A few points would be rounding; this is the difference between "filled" and "a band at the edge".
        var (reported, nodesOnly, _) = Measure("checkout-sequence",
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");

        Assert.True(reported - nodesOnly > 0.4,
            $"the two measurements differ by only {(reported - nodesOnly):P0}");
    }
}
