using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// An `unmetConstraints` entry must describe what actually happened, not what was intended.
///
/// `canvas.scaled` said "内容按 Tabloid 缩放以适配" — "content scaled to fit Tabloid". No scaling happens
/// anywhere in that path. The flag it reads only means "no standard sheet was large enough, so the
/// largest one was used". Measured on a 40-node chain: content 72.7" tall on a 17" page, 32 nodes
/// outside the sheet, and the report said the content had been fitted.
///
/// That is worse than saying nothing. A caller reading it would believe the deliverable is complete.
/// </summary>
public sealed class UnmetTruthfulnessTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static (ExecutionResult Result, Diagram Written) Run(string irJson, string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-unmet-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);
            return (result, new VsdxEngine().Read(path).Diagram);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string LongChain(int count)
    {
        var nodes = string.Join(",", Enumerable.Range(0, count)
            .Select(i => $$"""{"id":"n{{i}}","label":"Node{{i}}"}"""));
        var edges = string.Join(",", Enumerable.Range(0, count)
            .Select(i => $$"""{"id":"e{{i}}","from":"n{{i}}","to":"n{{(i + 1) % count}}"}"""));

        return $$"""{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{{nodes}}],"edges":[{{edges}}]}""";
    }

    [Fact]
    public void TheClaimAndTheFileAgreeAboutWhetherContentFits()
    {
        // The report and the package must tell the same story, whichever way it goes. Content too
        // large for the biggest sheet is a legitimate outcome — what is not legitimate is calling it
        // "fitted".
        var (result, written) = Run(LongChain(40), """{"specVersion":"1.0","canvas":{"mode":"paper"}}""");

        var claim = result.UnmetConstraints.FirstOrDefault(u => u.Code == "canvas.scaled");
        Assert.NotNull(claim);

        var page = written.Pages[0];
        int outside = page.Nodes.Count(n =>
            n.Bounds!.X - n.Bounds.W / 2 < 0 || n.Bounds.X + n.Bounds.W / 2 > page.Size!.Width ||
            n.Bounds.Y - n.Bounds.H / 2 < 0 || n.Bounds.Y + n.Bounds.H / 2 > page.Size.Height);

        bool claimsFit = claim!.Reason.Contains("缩放以适配", StringComparison.Ordinal)
                         || claim.Reason.Contains("已缩放", StringComparison.Ordinal);

        if (claimsFit)
            Assert.True(outside == 0, $"'{claim.Reason}' claims fitting, but {outside} node(s) are outside");

        // Otherwise the reason must say plainly that the content is not inside the sheet.
        else
            Assert.Contains("未缩小", claim.Reason);
    }

    [Fact]
    public void AnUnhonouredRequestSaysWhatWasNotDone()
    {
        var (result, _) = Run(LongChain(40), """{"specVersion":"1.0","canvas":{"mode":"paper"}}""");

        var claim = result.UnmetConstraints.FirstOrDefault(u => u.Code == "canvas.scaled");
        Assert.NotNull(claim);

        // It must say the content does not fit, not that it was made to.
        Assert.Contains("超出", claim!.Reason);
        Assert.DoesNotContain("缩放以适配", claim.Reason);
    }

    [Fact]
    public void AKnownLayoutFallsBackToSomethingThatIsReported()
    {
        // The model case: the fallback is named in `unmet` and the same name appears in `applied`.
        var (result, _) = Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "containers":[{"id":"g","label":"G"}],
         "nodes":[{"id":"a","label":"A","containerId":"g"}],"edges":[]}
        """, """{"specVersion":"1.0","layout":"nonsense"}""");

        var claim = result.UnmetConstraints.First(u => u.Code == "layout.unknown");
        Assert.Contains("Layered", claim.Reason);
        Assert.Contains(result.AppliedDesignNotes, n => n.Contains("Layered", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownPaletteIsReportedAndTheDefaultIsUsed()
    {
        var (result, written) = Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "nodes":[{"id":"a","label":"A","role":"service"}],"edges":[]}
        """, """{"specVersion":"1.0","theme":{"palette":"nope"}}""");

        Assert.Contains(result.UnmetConstraints, u => u.Code == "theme.palette-unknown");
        Assert.False(string.IsNullOrEmpty(written.Pages[0].Nodes.Single().Style?.Fill),
            "the palette was rejected but no colour was applied at all");
    }

    [Fact]
    public void EveryUnmetReasonStatesAnOutcome()
    {
        // A reason that only restates the request leaves the caller unable to tell what happened.
        var (result, _) = Run(LongChain(40),
            """{"specVersion":"1.0","canvas":{"mode":"paper"},"layout":"bogus","theme":{"palette":"bogus"}}""");

        foreach (var unmet in result.UnmetConstraints)
            Assert.False(string.IsNullOrWhiteSpace(unmet.Reason), $"{unmet.Code} has no reason");
    }
}
