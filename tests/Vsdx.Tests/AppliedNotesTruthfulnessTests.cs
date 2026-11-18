using System.Text.Json;
using System.Text.RegularExpressions;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every entry in `appliedDesignNotes` must describe something present in the written file.
///
/// The charter's prohibition is about silent omission; this is the opposite failure — claiming work
/// that did not happen. Both leave a caller unable to trust the report, and the second is worse because
/// it looks like diligence.
///
/// Each note below was checked against the produced package, not against the code path that writes it.
/// </summary>
public sealed class AppliedNotesTruthfulnessTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static (ExecutionResult Result, Diagram Written) Run(string irJson, string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-note-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);
            return (result, new VsdxEngine().Read(path).Diagram);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private const string TwoNodes = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "nodes":[{"id":"a","label":"Svc","role":"service"},{"id":"b","label":"DB","role":"database"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    [Fact]
    public void ThePaletteNoteNamesColoursThatAreActuallyThere()
    {
        var (result, written) = Run(TwoNodes, """{"specVersion":"1.0","theme":{"palette":"midnight"}}""");

        var note = result.AppliedDesignNotes.First(n => n.StartsWith("theme.palette=", StringComparison.Ordinal));
        var paletteName = note["theme.palette=".Length..];

        Assert.True(Palettes.TryGet(paletteName, out var palette));

        foreach (var node in written.Pages[0].Nodes)
        {
            var fill = node.Style?.Fill;
            Assert.False(string.IsNullOrEmpty(fill), $"'{node.Id}' has no fill despite the palette note");

            if (node.Type is not null && palette.Roles.TryGetValue(node.Type, out var expected))
                Assert.Equal(expected, fill, ignoreCase: true);
        }
    }

    [Fact]
    public void TheBackgroundNoteMatchesTheWrittenPage()
    {
        var (result, written) = Run(TwoNodes, """{"specVersion":"1.0","theme":{"background":"#F0E8D8"}}""");

        Assert.Contains("theme.background", result.AppliedDesignNotes);
        Assert.Equal("#F0E8D8", written.Pages[0].Background, ignoreCase: true);
    }

    [Fact]
    public void ThePaperNoteNamesTheSizeThePageActuallyIs()
    {
        var (result, written) = Run(TwoNodes, """{"specVersion":"1.0","canvas":{"mode":"paper"}}""");

        var note = result.AppliedDesignNotes.First(n => n.StartsWith("canvas.paper=", StringComparison.Ordinal));
        var sheet = note["canvas.paper=".Length..];

        var size = written.Pages[0].Size!;
        double longer = Math.Max(size.Width, size.Height);
        double shorter = Math.Min(size.Width, size.Height);

        var expected = sheet switch
        {
            "A5" => (8.27, 5.83),
            "A4" => (11.69, 8.27),
            "A3" => (16.54, 11.69),
            "A2" => (23.39, 16.54),
            _ => (longer, shorter)
        };

        Assert.Equal(expected.Item1, longer, 1);
        Assert.Equal(expected.Item2, shorter, 1);
    }

    [Fact]
    public void TheSizingNoteMeansBoxesWereDerivedFromText()
    {
        // A long label must widen its node; otherwise "sizing" would be a note about nothing.
        var (result, written) = Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "nodes":[{"id":"a","label":"这是一个非常长的节点名称用来测试文字推导"},{"id":"b","label":"短"}],
         "edges":[{"id":"e","from":"a","to":"b"}]}
        """, """{"specVersion":"1.0"}""");

        Assert.Contains("sizing", result.AppliedDesignNotes);

        var wide = written.Pages[0].Nodes.Single(n => n.Id == "a").Bounds!.W;
        var narrow = written.Pages[0].Nodes.Single(n => n.Id == "b").Bounds!.W;

        Assert.True(wide > narrow + 1.0, $"sizing claimed but widths are {wide:F2} and {narrow:F2}");
    }

    [Fact]
    public void TheBandsNoteMeansContainersWereStyled()
    {
        var (result, written) = Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "containers":[{"id":"g","label":"G"}],
         "nodes":[{"id":"a","label":"A","containerId":"g"}],"edges":[]}
        """, """{"specVersion":"1.0"}""");

        Assert.Contains("container.bands", result.AppliedDesignNotes);

        var style = written.Pages[0].Groups.Single().Style;
        Assert.False(string.IsNullOrEmpty(style?.Fill), "container.bands claimed but the container has no fill");
        Assert.True(style!.Bold, "container.bands claimed but the heading is not bold");
    }

    [Fact]
    public void AnOptionalNoteAppearsOnlyWhenTheStepRan()
    {
        // `decoration.clearance` and `label.spread` report a moved count; a run with nothing to move
        // must not claim them.
        var (result, _) = Run(TwoNodes, """{"specVersion":"1.0"}""");

        Assert.DoesNotContain("decoration.clearance", result.AppliedDesignNotes);
        Assert.DoesNotContain("label.spread", result.AppliedDesignNotes);
    }

    [Fact]
    public void EveryNoteHasARecognisableShape()
    {
        // A note that names no subject cannot be checked by anyone downstream.
        var (result, _) = Run(TwoNodes, """{"specVersion":"1.0","theme":{"palette":"enterprise"},"canvas":{"mode":"paper"}}""");

        var pattern = new Regex(@"^(?:[a-z]+\.[a-zA-Z]+(?:=[\w.\-]+)?|layout=\w+.*|sizing|typography)$");

        foreach (var note in result.AppliedDesignNotes)
            Assert.Matches(pattern, note);
    }
}
