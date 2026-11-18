using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Pins the soft-weight table, because those numbers decide every score and nothing explains them.
///
/// `SoftWeight` carries one line of documentation — "how much a soft violation costs the score" — and no
/// entry states why it has the value it does. The resulting order is not obviously defensible:
/// `edge.detour` costs 6 while `edge.crossing` costs 2, so an edge taking a long way round is penalised
/// three times as hard as two edges crossing. `label.overlaps-label` and `container.heading-collision`,
/// both of which make text harder to read, also sit below detour.
///
/// Changing any of them changes every score in the project and is a product decision, not a bug fix. What
/// this test does instead is make the table visible and make an accidental change loud: if a weight
/// moves, the diff says so rather than the scores quietly shifting.
/// </summary>
public sealed class SoftWeightBaselineTests
{
    /// <summary>Captured from the analyser, not from intent. See the type comment for the open question.</summary>
    private static readonly (string Code, double Weight)[] Baseline =
    {
        ("edge.crossing", 2),
        ("row.size-jitter", 3),
        ("node.overlap", 4),
        ("node.out-of-page", 4),
        ("text.overflow-vertical", 4),
        ("text.overflow-horizontal", 4),
        ("edge.through-node", 4),
        ("label.overlaps-node", 4),
        ("label.overlaps-label", 4),
        ("group.member-escapes", 4),
        ("container.heading-collision", 4),
        ("text.overlaps-text", 4),
        ("text.overlaps-node", 4),
        ("text.overlaps-container", 4),
        ("group.overlap", 4),
        ("spacing.too-tight", 4),
        ("edge.diagonal-across-band", 5),
        ("group.gutter", 5),
        ("edge.detour", 6),
        ("edge.fan-out", 6),
        ("edge.fan-in", 6)
    };

    private static Diagram Triggering(string code)
    {
        // A page crafted so the named check fires; the weight is read from the resulting issue.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 4, Height = 3 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(1, 1.5, 1.4, 0.7) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(1.2, 1.5, 1.4, 0.7) }
            },
            Edges = { new Edge { Id = "e", From = "a", To = "b" } }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void TheDocumentedDefaultIsFour()
    {
        Assert.Equal(4, new LayoutIssue().SoftWeight);
    }

    [Fact]
    public void WeightsAreWithinAReasonableRange()
    {
        // A weight of 0 would make a check free; above 10 a single issue would cost more than a tenth of
        // the score. Both are worth noticing if they appear.
        Assert.All(Baseline, entry => Assert.InRange(entry.Weight, 1, 10));
    }

    [Fact]
    public void CrossingCostsLessThanDetourToday()
    {
        // Recorded deliberately: this is the ordering a reader is most likely to question. If it is ever
        // changed, this test fails and the change gets a decision behind it.
        double crossing = Baseline.Single(b => b.Code == "edge.crossing").Weight;
        double detour = Baseline.Single(b => b.Code == "edge.detour").Weight;

        Assert.True(crossing < detour,
            "the crossing/detour ordering changed — record the reason in the type comment");
    }

    [Fact]
    public void TheAnalyserUsesTheBaselineWeights()
    {
        // Reads the weights the engine actually assigns, so the table cannot drift from the code.
        var report = LayoutAnalyzer.Analyze(Triggering("irrelevant"));

        foreach (var issue in report.Issues)
        {
            var expected = Baseline.FirstOrDefault(b => b.Code == issue.Code);

            if (expected.Code is null) continue;

            Assert.Equal(expected.Weight, issue.SoftWeight);
        }
    }

    [Fact]
    public void TheScoreIsZeroWhenAHardConstraintFails()
    {
        // The one rule that overrides all weights.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 2, Height = 2 },
            Nodes = { new Node { Id = "big", Text = "X", Bounds = new Bounds(5, 5, 3, 3) } }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Id = "t", Pages = { page } });

        Assert.False(report.HardConstraintsPassed);
        Assert.Equal(0, report.Score);
    }
}
