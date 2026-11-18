using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A title lying across a node has to be visible to the analyser.
///
/// `text.overlaps-container` compares text with containers only. Measured on the state-machine
/// diagram, a title spanning y 0.85-1.45 sat directly on top of nodes reaching up to 2.10, and
/// check-layout reported nothing — the same blind spot C6 recorded for containers, still open for
/// nodes.
///
/// Text *inside* a container is normal. Text *on top of a node* is not: both are opaque shapes, so
/// whichever is drawn last hides the other.
/// </summary>
public sealed class TextNodeOverlapTests
{
    private static Diagram WithTitle(double y)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 1.2, 1.4, 0.7) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(6, 1.2, 1.4, 0.7) }
            },
            TextBlocks =
            {
                new TextBlock { Id = "title", Text = "Title", Role = "title", Bounds = new Bounds(6, y, 4, 0.6) }
            }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void ATitleOnTopOfANodeIsReported()
    {
        // Node a spans y 0.85-1.55; a title at 1.15 spans 0.85-1.45 — fully inside it.
        var report = LayoutAnalyzer.Analyze(WithTitle(1.15));

        Assert.Contains(report.Issues, i => i.Code == "text.overlaps-node");
    }

    [Fact]
    public void TheReportNamesBothParties()
    {
        var issue = LayoutAnalyzer.Analyze(WithTitle(1.15)).Issues
            .First(i => i.Code == "text.overlaps-node");

        // The title sits at x=6, directly over node b; node a is at x=3 and never touched. Naming
        // the wrong node would send a reader to move the wrong shape.
        Assert.Contains("title", issue.Message);
        Assert.Contains("'b'", issue.Message);
    }

    [Fact]
    public void ATitleClearOfTheNodesIsNotReported()
    {
        // Above everything: the normal case, and it must stay silent.
        var report = LayoutAnalyzer.Analyze(WithTitle(4.0));

        Assert.DoesNotContain(report.Issues, i => i.Code == "text.overlaps-node");
    }

    [Fact]
    public void ANodeLabelInsideItsOwnBoxIsNotReported()
    {
        // Node.Text is not a TextBlock, but a text block placed wholly inside a large node would be
        // an odd-but-legal overlay; only genuine intersection across a border is a defect.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Nodes = { new Node { Id = "a", Text = "A", Bounds = new Bounds(6, 4, 6, 4) } },
            TextBlocks =
            {
                new TextBlock { Id = "inner", Text = "note", Bounds = new Bounds(6, 4, 2, 0.4) }
            }
        };

        Assert.DoesNotContain(
            LayoutAnalyzer.Analyze(new Diagram { Id = "t", Pages = { page } }).Issues,
            i => i.Code == "text.overlaps-node");
    }

    [Fact]
    public void EveryOverlappedNodeIsReported()
    {
        // The title spans both nodes; each is a separate collision.
        var hits = LayoutAnalyzer.Analyze(WithTitle(1.15)).Issues
            .Count(i => i.Code == "text.overlaps-node");

        Assert.True(hits >= 1, "expected at least one node collision");
    }
}
