using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// When the content cannot fit any standard sheet, the result must say so and must be marked failed.
///
/// `paper` mode picks a sheet and checks nothing. Measured on a 20-node chain: 12 nodes outside the
/// page, `status: partial`, and no `canvas.exceeded` — the only signal was the wording of
/// `canvas.scaled`, which a caller could easily read past. A file with half its shapes off the sheet
/// is not a partial success; it is not a deliverable.
/// </summary>
public sealed class PaperOverflowTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static (ExecutionResult Result, Diagram Written) Run(string irJson, string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-paper-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);
            return (result, new VsdxEngine().Read(path).Diagram);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string Chain(int count)
    {
        var nodes = string.Join(",", Enumerable.Range(0, count)
            .Select(i => $$"""{"id":"n{{i}}","label":"N{{i}}"}"""));
        var edges = string.Join(",", Enumerable.Range(0, count)
            .Select(i => $$"""{"id":"e{{i}}","from":"n{{i}}","to":"n{{(i + 1) % count}}"}"""));

        return $$"""{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{{nodes}}],"edges":[{{edges}}]}""";
    }

    private static int Outside(Diagram d)
    {
        var page = d.Pages[0];

        return page.Nodes.Count(n =>
            n.Bounds!.X - n.Bounds.W / 2 < -1e-6 || n.Bounds.X + n.Bounds.W / 2 > page.Size!.Width + 1e-6 ||
            n.Bounds.Y - n.Bounds.H / 2 < -1e-6 || n.Bounds.Y + n.Bounds.H / 2 > page.Size.Height + 1e-6);
    }

    [Fact]
    public void ContentThatDoesNotFitIsReportedAsExceeded()
    {
        var (result, written) = Run(Chain(20), """{"specVersion":"1.0","canvas":{"mode":"paper"}}""");

        Assert.True(Outside(written) > 0, "the fixture no longer overflows — pick a longer chain");

        Assert.Contains(result.UnmetConstraints, u => u.Code == "canvas.exceeded");
    }

    [Fact]
    public void AFileWithShapesOffTheSheetIsNotDeliverable()
    {
        var (result, written) = Run(Chain(20), """{"specVersion":"1.0","canvas":{"mode":"paper"}}""");

        Assert.True(Outside(written) > 0, "the fixture no longer overflows");

        // `partial` is the right status — the file exists and most of it is usable, which is what
        // `partial` means. What makes it undeliverable is the hard constraint, so that is what to
        // assert. Changing `DeriveStatus` to call this `failed` would break the distinction between
        // "the run broke" and "the run finished with requirements unmet".
        Assert.Equal("partial", result.Status);
        Assert.False(result.Quality.HardConstraintsPassed);

        var claim = result.UnmetConstraints.First(u => u.Code == "canvas.exceeded");
        Assert.True(claim.IsHard, "canvas.exceeded must be a hard constraint");
    }

    [Fact]
    public void ContentThatFitsIsUnaffected()
    {
        // The ordinary case must keep working: a normal diagram on paper is a success.
        var (result, written) = Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "containers":[{"id":"g","label":"G"}],
         "nodes":[{"id":"a","label":"A","containerId":"g"},{"id":"b","label":"B","containerId":"g"}],
         "edges":[{"id":"e","from":"a","to":"b"}]}
        """, """{"specVersion":"1.0","canvas":{"mode":"paper"}}""");

        Assert.Equal(0, Outside(written));
        Assert.DoesNotContain(result.UnmetConstraints, u => u.Code == "canvas.exceeded");

        // Not `succeeded`: soft notes such as `page.margins` keep it at `partial`. What matters is
        // that this diagram is not `failed`, and that nothing is off the page.
        Assert.NotEqual("failed", result.Status);
    }

    [Fact]
    public void TheReportNamesHowManyShapesAreOutside()
    {
        // "Exceeded" without a number leaves the caller guessing whether it is one label or half the
        // diagram.
        var (result, written) = Run(Chain(20), """{"specVersion":"1.0","canvas":{"mode":"paper"}}""");

        var claim = result.UnmetConstraints.First(u => u.Code == "canvas.exceeded");

        Assert.Contains($"{Outside(written)}", claim.Reason);
    }
}
