using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `--layout auto` is not worse than hand-written coordinates; pairing it with `paper` is.
///
/// The independent agent measured auto at 84 against its own hand-placed 96 and concluded the guide was wrong to
/// recommend auto. Half of that gap was a real defect — a page title swallowed by a band, fixed since — and the
/// rest was the canvas mode.
///
/// Measured on the same IR:
///
///   layout=auto  canvas=auto    98    page 8.55 x 8.72, fitted to content
///   layout=auto  canvas=fixed   98    same
///   layout=auto  canvas=paper   93    page snapped to A4, 29% utilised
///   hand-placed coordinates     96
///
/// So auto is fine. What costs points is insisting on a standard sheet when the content does not fit one — which
/// is a real trade-off, not a defect: the caller asked for A4, and the engine cannot both honour that and fill
/// it. `paper` is the right choice when A4 is a requirement and the content is A4-shaped; `auto` is right when
/// the diagram decides its own size.
/// </summary>
public sealed class AutoVsPaperTests
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

    private static (int Score, double Width, double Height) Run(string specJson)
    {
        var irPath = Path.Combine(Root(), "docs", "research", "experiments", "agent-trial-01", "auto.ir.json");

        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(File.ReadAllText(irPath), Json)!;
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-autopaper-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, output);
            var page = new VsdxEngine().Read(output).Diagram.Pages[0];

            return (result.Quality.Score, page.Size!.Width, page.Size.Height);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public void AutoLayoutWithAContentSizedCanvasScoresHigherThanPaper()
    {
        var contentSized = Run("""{"specVersion":"1.0","layout":"auto","canvas":{"mode":"auto"}}""");
        var paper = Run("""{"specVersion":"1.0","layout":"auto","canvas":{"mode":"paper"}}""");

        Assert.True(contentSized.Score > paper.Score,
            $"content-sized {contentSized.Score} vs paper {paper.Score}");
    }

    [Fact]
    public void AContentSizedCanvasIsNotAStandardSheet()
    {
        // The trade-off in one assertion: the better score comes from a page that is not A4.
        var contentSized = Run("""{"specVersion":"1.0","layout":"auto","canvas":{"mode":"auto"}}""");

        bool isA4 = Math.Abs(contentSized.Width - 11.69) < 0.1 || Math.Abs(contentSized.Height - 8.27) < 0.1;

        Assert.False(isA4, $"expected a fitted page, got {contentSized.Width:F2} x {contentSized.Height:F2}");
    }

    [Fact]
    public void PaperModeDoesSnapToAStandardSheet()
    {
        var paper = Run("""{"specVersion":"1.0","layout":"auto","canvas":{"mode":"paper"}}""");

        double[] aSizes = { 8.27, 11.69, 16.54, 23.39 };

        bool snapped = aSizes.Any(s => Math.Abs(paper.Width - s) < 0.1)
                    && aSizes.Any(s => Math.Abs(paper.Height - s) < 0.1);

        Assert.True(snapped, $"expected A-series, got {paper.Width:F2} x {paper.Height:F2}");
    }
}
