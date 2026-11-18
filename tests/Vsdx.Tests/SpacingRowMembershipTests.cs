using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The horizontal-gap check must only compare nodes that actually share a row.
///
/// It tested `|dy| &lt; (h_a + h_b) / 2`, which for a tall node and a short one is a large threshold.
/// Measured on the data-flow diagram: 'web' (a 1.85" circle) and 'dw' (a 0.55" box) sit 0.28" apart
/// vertically yet were treated as row neighbours, so a perfectly good layout was reported as three
/// counts of "spacing.too-tight" — and the flow engine scored 79 on them.
///
/// Row membership is about centre lines lining up, not about tall shapes whose bounding boxes happen
/// to overlap.
/// </summary>
public sealed class SpacingRowMembershipTests
{
    private static Diagram TwoNodes(Node a, Node b)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Nodes = { a, b },
            Edges = { new Edge { Id = "e", From = a.Id, To = b.Id } }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void ATallNodeAndAShortOneInDifferentRowsDoNotTriggerTheGapCheck()
    {
        // Same x, 0.28" apart vertically: close enough that the old rule called them a row.
        var tall = new Node { Id = "tall", Text = "T", Bounds = new Bounds(4, 4.00, 1.85, 1.85) };
        var low = new Node { Id = "low", Text = "L", Bounds = new Bounds(4, 3.72, 1.30, 0.55) };

        var report = LayoutAnalyzer.Analyze(TwoNodes(tall, low));

        Assert.DoesNotContain(report.Issues, i => i.Code == "spacing.too-tight");
    }

    [Fact]
    public void GenuineRowNeighboursWithNoGapAreStillReported()
    {
        // The check must keep working: two boxes on the same centre line, touching.
        var left = new Node { Id = "l", Text = "L", Bounds = new Bounds(3.0, 4, 1.30, 0.55) };
        var right = new Node { Id = "r", Text = "R", Bounds = new Bounds(4.30, 4, 1.30, 0.55) };

        var report = LayoutAnalyzer.Analyze(TwoNodes(left, right));

        Assert.Contains(report.Issues, i => i.Code == "spacing.too-tight");
    }

    [Fact]
    public void ASmallVerticalOffsetWithinARowIsStillARow()
    {
        // Real layouts leave a few hundredths of drift; that must not hide a genuine gap problem.
        var left = new Node { Id = "l", Text = "L", Bounds = new Bounds(3.0, 4.00, 1.30, 0.55) };
        var right = new Node { Id = "r", Text = "R", Bounds = new Bounds(4.32, 4.05, 1.30, 0.55) };

        var report = LayoutAnalyzer.Analyze(TwoNodes(left, right));

        Assert.Contains(report.Issues, i => i.Code == "spacing.too-tight");
    }

    [Fact]
    public void NodesFarApartOnBothAxesAreNotRowNeighbours()
    {
        var a = new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 6, 1.30, 0.55) };
        var b = new Node { Id = "b", Text = "B", Bounds = new Bounds(9, 2, 1.30, 0.55) };

        Assert.DoesNotContain(LayoutAnalyzer.Analyze(TwoNodes(a, b)).Issues,
            i => i.Code == "spacing.too-tight");
    }
}
