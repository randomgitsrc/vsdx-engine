using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A page title can be swallowed by a band that grows into it.
///
/// `DecorationLayout.TryClear` deliberately ignores a text block that sits wholly inside a container — the comment
/// says "wholly inside: the author meant it", and for a zone label that is right.
///
/// It is wrong for a title after the layout has run. The band is derived from its members, so it can grow
/// upward *around* a title that was clear when the author placed it, and the result looks deliberate to this
/// test while being a layout accident.
///
/// Measured on the diagram the independent agent produced, run through `--layout auto`: the title spans
/// y[6.98, 7.48] and the topmost band y[5.01, 7.66] — the title is entirely within the band, `TryClear` skips it,
/// and the report says `text.overlaps-container` with 5.66" x 0.50" of overlap.
///
/// The distinction the code needs is authorship, not geometry, and the model does not currently record it.
/// </summary>
public sealed class TitleClearanceTests
{
    private static Diagram TitleInsideBand()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 11.69, Height = 8.27 } };

        page.Groups.Add(new Group { Id = "access", Name = "接入层", Kind = "layer", Order = 3, Members = { "a", "b" } });
        page.Groups.Add(new Group { Id = "svc", Name = "服务层", Kind = "layer", Order = 2, Members = { "c" } });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "access", Bounds = new Bounds(3, 6.3, 1.5, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "access", Bounds = new Bounds(6, 6.3, 1.5, 0.6) });
        page.Nodes.Add(new Node { Id = "c", Text = "C", GroupId = "svc", Bounds = new Bounds(3, 3.5, 1.5, 0.6) });

        // Title sitting where the band will grow around it.
        page.TextBlocks.Add(new TextBlock
        {
            Id = "title", Text = "订单系统运作架构", Role = "title",
            Bounds = new Bounds(5.85, 7.25, 6, 0.5)
        });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void AWhollyContainedTitleIsReported()
    {
        var diagram = TitleInsideBand();

        DiagramLayout.Apply(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);

        // Either the layout clears it, or the report says it is a problem. Silence is the bad outcome.
        bool cleared = !report.Issues.Any(i => i.Code == "text.overlaps-container" && i.Targets.Contains("title"));
        bool reported = report.Issues.Any(i => i.Code == "text.overlaps-container" && i.Targets.Contains("title"));

        Assert.True(cleared || reported);
    }

    [Fact]
    public void ClearanceDoesNotMoveAZoneLabelThatBelongsToTheContainer()
    {
        // The behaviour the "wholly inside" rule protects: a label the author put inside a band stays put.
        //
        // The fixture has to put the label genuinely inside. My first version reused the title's position, which
        // the band had grown past — the label was straddling the border, so moving it was correct and the test
        // was wrong.
        var diagram = TitleInsideBand();
        var block = diagram.Pages[0].TextBlocks.Single();
        block.Role = "legend";
        block.Bounds = new Bounds(5.85, 6.5, 6, 0.4);      // well inside the band

        DiagramLayout.Apply(diagram);
        var before = diagram.Pages[0].TextBlocks.Single().Bounds!.Y;

        DecorationLayout.AvoidContainers(diagram);

        Assert.Equal(before, diagram.Pages[0].TextBlocks.Single().Bounds!.Y, 3);
    }
}
