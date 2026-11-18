using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The analyser must judge a label where the file puts it.
///
/// The writer places a label at `edge.LabelPosition ?? 0.5` along the route. The analyser used a
/// hardcoded 0.5 in two places and never read the field, so moving a label had no effect on the
/// verdict: measured, setting positions 0.3 and 0.7 still produced the same two overlap warnings.
///
/// That makes the field useless for fixing an overlap — the caller changes it, the check cannot see
/// the change — and it means the analyser reports on a diagram that was not built.
///
/// Most diagrams keep the default, so the defect only surfaces on the ones where someone tried to
/// move a label out of the way.
/// </summary>
public sealed class LabelPositionConsistencyTests
{
    private static Diagram TwoEdges(double? first, double? second)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 4, 1.4, 0.7) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(9, 4, 1.4, 0.7) }
            },
            Edges =
            {
                new Edge { Id = "e1", From = "a", To = "b", Label = "gRPC", LabelPosition = first },
                new Edge { Id = "e2", From = "a", To = "b", Label = "REST", LabelPosition = second }
            }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void MovingOneLabelApartClearsTheOverlap()
    {
        // Same endpoints, so the only thing that can separate them is the position along the route.
        var report = LayoutAnalyzer.Analyze(TwoEdges(first: 0.2, second: 0.8));

        Assert.DoesNotContain(report.Issues, i => i.Code == "label.overlaps-label");
    }

    [Fact]
    public void LeavingBothAtTheSamePositionStillOverlaps()
    {
        var report = LayoutAnalyzer.Analyze(TwoEdges(first: 0.5, second: 0.5));

        Assert.Contains(report.Issues, i => i.Code == "label.overlaps-label");
    }

    [Fact]
    public void NullMeansTheDefaultAndIsTreatedLikeHalf()
    {
        // A null position is 0.5 by the writer's own rule, so the analyser must agree.
        var withNull = LayoutAnalyzer.Analyze(TwoEdges(first: null, second: 0.5));
        var explicitHalf = LayoutAnalyzer.Analyze(TwoEdges(first: 0.5, second: 0.5));

        bool nullOverlaps = withNull.Issues.Any(i => i.Code == "label.overlaps-label");
        bool halfOverlaps = explicitHalf.Issues.Any(i => i.Code == "label.overlaps-label");

        Assert.Equal(halfOverlaps, nullOverlaps);
    }

    [Fact]
    public void OutOfRangePositionsAreClampedTheSameWayTheWriterClamps()
    {
        // The writer clamps to 0.05..0.95; the analyser must not invent a different place.
        var extreme = LayoutAnalyzer.Analyze(TwoEdges(first: 100, second: 100));
        var clamped = LayoutAnalyzer.Analyze(TwoEdges(first: 0.95, second: 0.95));

        Assert.Equal(
            clamped.Issues.Count(i => i.Code == "label.overlaps-label"),
            extreme.Issues.Count(i => i.Code == "label.overlaps-label"));
    }
}
