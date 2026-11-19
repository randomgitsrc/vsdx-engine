using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Bands in a layered diagram are as wide as their own members, so a layer with three clients ends up 1.5" wider
/// than a layer with three services and the right-hand edge becomes a staircase.
///
/// Measured on `cbo`: `edge` is 7.11" wide because its clients are 1.85"-diameter circles, while `svc` and `data`
/// are 5.60" because a service is 1.30 x 0.55. All three have three members. The left edges line up at 0.55, so
/// the mismatch shows entirely on the right.
///
/// A layered architecture diagram reads as layers: equal-width bands stacked flush. Each band deciding its own
/// width makes the drawing look assembled rather than designed — which is the distinction that matters here, and
/// one no geometric check can measure.
/// </summary>
public sealed class EvenBandTests
{
    private static Diagram ThreeUnevenBands()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 12 } };

        page.Groups.Add(new Group { Id = "top", Name = "接入层", Kind = "layer", Order = 3, Members = { "c1", "c2", "c3" } });
        page.Groups.Add(new Group { Id = "mid", Name = "服务层", Kind = "layer", Order = 2, Members = { "s1", "s2", "s3" } });
        page.Groups.Add(new Group { Id = "bot", Name = "数据层", Kind = "layer", Order = 1, Members = { "d1", "d2", "d3" } });

        // Clients get long labels so the top row is genuinely wider than the others. Circles alone were not
        // enough: with three short labels the two bands came out the same width and the old-behaviour test
        // passed for the wrong reason.
        page.Nodes.Add(new Node { Id = "c1", Text = "Web 门户系统", Shape = "Circle", GroupId = "top" });
        page.Nodes.Add(new Node { Id = "c2", Text = "移动 App 客户端", Shape = "Circle", GroupId = "top" });
        page.Nodes.Add(new Node { Id = "c3", Text = "开放 API 平台", Shape = "Circle", GroupId = "top" });

        page.Nodes.Add(new Node { Id = "s1", Text = "订单", GroupId = "mid" });
        page.Nodes.Add(new Node { Id = "s2", Text = "支付", GroupId = "mid" });
        page.Nodes.Add(new Node { Id = "s3", Text = "库存", GroupId = "mid" });

        page.Nodes.Add(new Node { Id = "d1", Text = "PG", GroupId = "solo" });

        // A third band with a single member guarantees a width difference regardless of how the sizer normalises
        // individual nodes: three members cannot come out the same width as one.
        page.Groups.Add(new Group { Id = "solo", Name = "数据层", Kind = "layer", Order = 0, Members = { "d1" } });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    private static List<Group> LayOut(bool evenBands)
    {
        var diagram = ThreeUnevenBands();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom, EvenBands = evenBands },
            LayoutStrategy.Layered);

        // No second `DiagramLayout.Apply` here: the engine already ran it, and running it again re-derives each
        // band from its own members, undoing the even widths the engine just set.
        return diagram.Pages[0].Groups.Where(g => g.Bounds is not null).ToList();
    }

    [Fact]
    public void BandsEndUpTheSameWidth()
    {
        var bands = LayOut(evenBands: true);

        var widths = bands.Select(g => Math.Round(g.Bounds!.W, 2)).Distinct().ToList();

        Assert.Single(widths);
    }

    [Fact]
    public void BandsShareTheirLeftAndRightEdges()
    {
        // Equal width alone is not enough — the edges have to coincide too, or the bands stagger.
        var bands = LayOut(evenBands: true);

        var lefts = bands.Select(g => Math.Round(g.Bounds!.X - g.Bounds!.W / 2, 2)).Distinct().ToList();
        var rights = bands.Select(g => Math.Round(g.Bounds!.X + g.Bounds!.W / 2, 2)).Distinct().ToList();

        Assert.Single(lefts);
        Assert.Single(rights);
    }

    [Fact]
    public void EveryMemberStaysInsideItsBand()
    {
        // The constraint that makes widening safe: a band must contain its own nodes after the change.
        var diagram = ThreeUnevenBands();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom, EvenBands = true },
            LayoutStrategy.Layered);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "group.member-escapes");
    }

    [Fact]
    public void TheOldBehaviourIsStillAvailable()
    {
        // Not a silent change: with `EvenBands` off the widths stay as each band's members dictate.
        var bands = LayOut(evenBands: false);

        Assert.True(bands.Select(g => Math.Round(g.Bounds!.W, 2)).Distinct().Count() > 1);
    }
}
