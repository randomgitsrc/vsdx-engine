using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `edge.fan-in` and `edge.fan-out` fire when one node carries more than `MaxFanOut` connectors.
///
/// Neither had a test that triggered it — the only references were as rows in the weight table — so
/// nothing would have noticed if the comparison were `&lt;` instead of `&gt;`, or if the threshold were
/// off by one. A sweep of every example against every layout strategy never produced either code, which
/// is how they were found: ten of the twenty-four checks had never fired in any scenario.
///
/// The threshold is `&gt; 5`, so six connectors report and five do not. Both boundaries are tested, since
/// an off-by-one here is invisible on every real diagram.
/// </summary>
public sealed class FanDensityTests
{
    private static Diagram Star(int spokes, bool outward)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 20, Height = 16 },
            Nodes = { new Node { Id = "hub", Text = "hub", Bounds = new Bounds(10, 8, 1.4, 0.6) } }
        };

        for (int i = 0; i < spokes; i++)
        {
            var id = $"s{i}";
            page.Nodes.Add(new Node { Id = id, Text = id, Bounds = new Bounds(4 + i * 2.0, 3, 1.2, 0.5) });

            page.Edges.Add(outward
                ? new Edge { Id = $"e{i}", From = "hub", To = id }
                : new Edge { Id = $"e{i}", From = id, To = "hub" });
        }

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void SixOutgoingConnectorsReportAFanOut()
    {
        var report = LayoutAnalyzer.Analyze(Star(6, outward: true));

        Assert.Contains(report.Issues, i => i.Code == "edge.fan-out");
    }

    [Fact]
    public void FiveOutgoingConnectorsDoNot()
    {
        var report = LayoutAnalyzer.Analyze(Star(5, outward: true));

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.fan-out");
    }

    [Fact]
    public void SixIncomingConnectorsReportAFanIn()
    {
        var report = LayoutAnalyzer.Analyze(Star(6, outward: false));

        Assert.Contains(report.Issues, i => i.Code == "edge.fan-in");
    }

    [Fact]
    public void FiveIncomingConnectorsDoNot()
    {
        var report = LayoutAnalyzer.Analyze(Star(5, outward: false));

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.fan-in");
    }

    [Fact]
    public void TheMessageStatesTheCountAndTheLimit()
    {
        // A warning that does not say "6 connectors, limit 5" leaves the caller to guess the threshold.
        var issue = LayoutAnalyzer.Analyze(Star(7, outward: true))
            .Issues.First(i => i.Code == "edge.fan-out");

        Assert.Contains("7", issue.Message);
        Assert.Contains("5", issue.Message);
    }

    [Fact]
    public void BothDirectionsAreWeighedEqually()
    {
        // The two codes exist as a pair; if one lost its weight the asymmetry would be invisible.
        double outWeight = LayoutAnalyzer.Analyze(Star(6, outward: true))
            .Issues.First(i => i.Code == "edge.fan-out").SoftWeight;

        double inWeight = LayoutAnalyzer.Analyze(Star(6, outward: false))
            .Issues.First(i => i.Code == "edge.fan-in").SoftWeight;

        Assert.Equal(outWeight, inWeight);
    }
}
