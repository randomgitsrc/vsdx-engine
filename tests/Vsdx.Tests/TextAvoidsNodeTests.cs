using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A text block placed inside a band will sit on top of the band's nodes, and nothing moves it.
///
/// `DecorationLayout` has `AvoidContainers` for band borders and `SpreadLabels` for connector labels. Neither
/// looks at nodes, so a legend or note whose box lands inside a band is drawn over whatever is there. The
/// analyser reports `text.overlaps-node`; the layout does not act on it.
///
/// This surfaced on `deploy-topology` after I changed band ordering: the legend at y=0.85 now falls inside the
/// data band (`y[0.55, 1.91]`) and overlaps four of its nodes. The ordering change exposed it, it did not create
/// it — the same legend sat over empty space before because the band happened to be narrower.
/// </summary>
public sealed class TextAvoidsNodeTests
{
    private static Diagram LegendInsideBand()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 10 } };

        page.Groups.Add(new Group { Id = "band", Name = "数据层", Kind = "layer", Order = 1, Members = { "a", "b" } });
        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "band", Bounds = new Bounds(3, 2, 1.6, 0.7) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "band", Bounds = new Bounds(7, 2, 1.6, 0.7) });

        // A legend whose box lands right on top of the nodes.
        page.TextBlocks.Add(new TextBlock
        {
            Id = "legend", Text = "蓝 = 服务   绿 = 数据", Role = "legend",
            Bounds = new Bounds(5, 2.0, 5, 0.35)
        });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void TheOverlapIsDetected()
    {
        // First, that the analyser sees it — otherwise the rest of this file tests nothing.
        var diagram = LegendInsideBand();
        DiagramLayout.Apply(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.Contains(report.Issues, i => i.Code == "text.overlaps-node");
    }

    [Fact]
    public void NothingMovesTheTextOffTheNodes()
    {
        // Recording current behaviour: the decoration passes run, and the overlap survives them.
        var diagram = LegendInsideBand();
        DiagramLayout.Apply(diagram);

        DecorationLayout.AvoidContainers(diagram);
        DecorationLayout.SpreadLabels(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.Contains(report.Issues, i => i.Code == "text.overlaps-node");
    }
}
