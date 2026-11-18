using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `repair` accepts a change that reduces the issue count even when the score falls.
///
/// `VersionStore.Evaluate` takes a candidate when it introduces no new hard violation and the issue total
/// drops — the score is never consulted. Constructed rather than assumed:
///
///   current     2 × edge.crossing (weight 2)   score 96
///   candidate   1 × edge.detour   (weight 6)   score 94
///
/// One issue instead of two, two points lower. Accepted with the reason "改善了既有问题且未引入新问题".
///
/// That is deliberate: `repair` exists to clear a named issue, not to raise a number, and the score is
/// documented as advisory. What it does mean is that a caller who runs `repair` for its score can get a
/// worse one — which is why `attempts[]` reports the before and after on every step.
/// </summary>
public sealed class RepairAcceptanceTests
{
    private static LayoutReport Report(params (string Code, string Severity, double Weight)[] issues)
    {
        var report = new LayoutReport();

        foreach (var (code, severity, weight) in issues)
            report.Issues.Add(new LayoutIssue
            {
                Code = code, Severity = severity, SoftWeight = weight, Page = "p"
            });

        return report;
    }

    private static VersionStore StoreWith(LayoutReport baseline)
    {
        var store = new VersionStore();
        store.Capture(new Diagram
        {
            Id = "t",
            Pages = { new Page { Name = "p", Size = new PageSize { Width = 10, Height = 8 } } }
        }, "base", baseline);

        return store;
    }

    [Fact]
    public void FewerIssuesWinsEvenWhenTheScoreDrops()
    {
        var current = Report(("edge.crossing", "warning", 2), ("edge.crossing", "warning", 2));
        var candidate = Report(("edge.detour", "warning", 6));

        Assert.Equal(96, current.Score);
        Assert.Equal(94, candidate.Score);

        var decision = StoreWith(current).Evaluate(candidate, "edge.crossing");

        Assert.True(decision.Accepted);
    }

    [Fact]
    public void RemovingTheTargetIssueIsEnoughOnItsOwn()
    {
        // Even when the total is unchanged and each remaining issue is heavier: `edge.crossing` is gone,
        // so the intent was met. Third acceptance path, independent of the count.
        var current = Report(("edge.crossing", "warning", 2), ("edge.crossing", "warning", 2));

        var sameCountHeavier = Report(("edge.detour", "warning", 6), ("edge.detour", "warning", 6));

        Assert.Equal(2, sameCountHeavier.Issues.Count);
        Assert.True(sameCountHeavier.Score < current.Score);

        Assert.True(StoreWith(current).Evaluate(sameCountHeavier, "edge.crossing").Accepted);
    }

    [Fact]
    public void WithoutProgressTheCandidateIsRejected()
    {
        // Same issue codes, same count: nothing improved, so nothing is accepted. This is what stops the
        // patch spiral — a change that fixes nothing cannot be justified by a nicer-looking score.
        var current = Report(("edge.crossing", "warning", 2), ("edge.detour", "warning", 6));
        var noChange = Report(("edge.crossing", "warning", 2), ("edge.detour", "warning", 6));

        Assert.False(StoreWith(current).Evaluate(noChange, "edge.crossing").Accepted);
    }

    [Fact]
    public void ANewHardViolationStillBlocks()
    {
        // The one rule that overrides the count: content that becomes undeliverable is never accepted.
        var current = Report(("edge.crossing", "warning", 2), ("edge.crossing", "warning", 2));
        var candidate = Report(("node.overlap", "error", 4));

        Assert.False(StoreWith(current).Evaluate(candidate, "edge.crossing").Accepted);
    }

    [Fact]
    public void TheReasonStatesWhatWasMeasured()
    {
        var current = Report(("edge.crossing", "warning", 2), ("edge.crossing", "warning", 2));
        var candidate = Report(("edge.detour", "warning", 6));

        var decision = StoreWith(current).Evaluate(candidate, "edge.crossing");

        Assert.Contains("改善", decision.Reason);
        Assert.DoesNotContain("分数", decision.Reason);
    }
}
