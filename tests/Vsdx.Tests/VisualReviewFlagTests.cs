using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `requiresVisualReview` must distinguish diagrams, or it says nothing.
///
/// It was set to `true` unconditionally — every result carried it, including a one-node diagram with
/// a score of 0. A flag that is always true is not information; a caller cannot use it to decide
/// which outputs to open and look at.
///
/// The honest content of the flag is narrow: "this diagram has issues the analyser can see *and*
/// issues it structurally cannot see". The ones it cannot see are the ones whose subjects do not
/// exist in the model — connection labels are drawn at render time and appear in no bounding box, so
/// a label sitting over a text block is invisible to every check.
/// </summary>
public sealed class VisualReviewFlagTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static QualityOutcome Quality(string irJson, string specJson = """{"specVersion":"1.0"}""")
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-vr-{Guid.NewGuid():N}.vsdx");
        try { return new ProtocolRunner().Execute(ir, spec, path).Quality; }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TheFlagTracksTheViolationCount()
    {
        // The flag's meaning is exactly "there is something the analyser flagged, and therefore
        // something a human may see that it could not describe". Asserting it against the same
        // counts the result already carries keeps the two from drifting apart.
        var quality = Quality(File.ReadAllText(
            Path.Combine(Root(), "benchmarks", "org", "simple.json")),
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");

        bool hasViolations = quality.HardViolations + quality.SoftViolations > 0;

        Assert.Equal(hasViolations, quality.RequiresVisualReview);
    }

    [Fact]
    public void ADiagramWithWarningsDoesAskForReview()
    {
        // Broken spacing produces warnings; each one is a place a human may see something the
        // analyser does not.
        var quality = Quality("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "nodes":[{"id":"a","label":"A","anchor":{"x":3,"y":5}},
                  {"id":"b","label":"B","anchor":{"x":3.1,"y":5}}],
         "edges":[]}
        """, """{"specVersion":"1.0","layout":"manual"}""");

        Assert.True(quality.SoftViolations > 0 || quality.HardViolations > 0);
        Assert.True(quality.RequiresVisualReview);
    }

    [Fact]
    public void TheFlagIsExplainedWhereACallerWillReadIt()
    {
        // A bare boolean invites guessing; the surrounding contract has to say what it means.
        var skill = File.ReadAllText(Path.Combine(Root(), "skill", "SKILL.md"));

        Assert.Contains("requiresVisualReview", skill);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
