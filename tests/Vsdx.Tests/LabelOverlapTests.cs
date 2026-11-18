using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Two edge labels landing on each other is a defect the analyser never looked for.
///
/// Measured on the cross-border order diagram: labels "gRPC" and "REST" sat 0.32" apart while each
/// is roughly 0.5" wide, so they overlapped on the rendered page. check-layout reported 91/100 with
/// no warning, because the only label check was `label.overlaps-node` — labels were never compared
/// with other labels.
///
/// The object pairs the analyser covers are now: node/node, group/group, label/node, text/group,
/// and label/label. Text/node is still uncovered and is recorded separately.
/// </summary>
public sealed class LabelOverlapTests
{
    /// <summary>
    /// Two edges converging on the same target from different sources.
    ///
    /// This is the shape that produced the overlap in the real diagram: the labels sit at each
    /// edge's midpoint, and edges aimed at one node have midpoints near each other even when their
    /// sources are far apart. Fanning out from a single source does not reproduce it, because the
    /// midpoint of a short edge and a long one diverge — and pulling the targets close enough to
    /// coincide makes the *nodes* overlap instead, which is a different defect.
    /// </summary>
    private static Diagram Coincident()
    {
        // Two edges between the same pair of nodes. Their midpoints are the same point, so the
        // labels necessarily collide — the extreme case of the real diagram's "several edges
        // converging on one node" pattern.
        //
        // Earlier fixtures tried to approach this with nearby-but-distinct endpoints; measured, the
        // label midpoints stayed 0.3"-1.5" apart, which is well outside a 0.16" label height, so
        // they never overlapped and the tests were asserting nothing.
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
                new Edge { Id = "e1", From = "a", To = "b", Label = "gRPC" },
                new Edge { Id = "e2", From = "a", To = "b", Label = "REST" }
            }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    private static Diagram Converging(double sourceSpread)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 4 + sourceSpread, 1.4, 0.7) },
                new Node { Id = "c", Text = "C", Bounds = new Bounds(2, 4 - sourceSpread, 1.4, 0.7) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(10, 4, 1.4, 0.7) }
            },
            Edges =
            {
                new Edge { Id = "e1", From = "a", To = "b", Label = "gRPC" },
                new Edge { Id = "e2", From = "c", To = "b", Label = "REST" }
            }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void LabelsLandingOnEachOtherAreReported()
    {
        // Targets are 0.3 apart vertically, so both midpoints sit within a label's height.
        var report = LayoutAnalyzer.Analyze(Coincident());

        Assert.Contains(report.Issues, i => i.Code == "label.overlaps-label");
    }

    [Fact]
    public void TheReportNamesBothEdges()
    {
        var issue = LayoutAnalyzer.Analyze(Coincident()).Issues
            .First(i => i.Code == "label.overlaps-label");

        Assert.Contains("e1", issue.Message);
        Assert.Contains("e2", issue.Message);
    }

    [Fact]
    public void WellSeparatedLabelsAreNotReported()
    {
        // A warning that fires on healthy diagrams is noise and stops being read.
        var report = LayoutAnalyzer.Analyze(Converging(sourceSpread: 2.6));

        Assert.DoesNotContain(report.Issues, i => i.Code == "label.overlaps-label");
    }

    [Fact]
    public void EachPairIsReportedOnce()
    {
        // An overlap is one problem, not two — reported once per pair regardless of iteration order.
        var hits = LayoutAnalyzer.Analyze(Coincident()).Issues
            .Where(i => i.Code == "label.overlaps-label").ToList();

        Assert.Single(hits);
    }

    [Fact]
    public void EdgesWithoutLabelsAreIgnored()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 8 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(1.5, 4, 1.4, 0.7) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(8.5, 4, 1.4, 0.7) }
            },
            Edges =
            {
                new Edge { Id = "e1", From = "a", To = "b" },
                new Edge { Id = "e2", From = "a", To = "b" }
            }
        };

        Assert.DoesNotContain(LayoutAnalyzer.Analyze(new Diagram { Id = "t", Pages = { page } }).Issues,
            i => i.Code == "label.overlaps-label");
    }
}
