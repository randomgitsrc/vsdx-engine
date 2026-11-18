using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Two text blocks lying on each other must be reported.
///
/// `ClampDecorationsToPage` clamps each decoration independently, so when several are pushed past the
/// same edge they all land on that edge and their relative spacing collapses. Measured on
/// examples/phase8/arch.ir.json: title at y=9.25 (h 0.60) and subtitle at y=9.35 (h 0.40) — 0.40" of
/// overlap — plus legend/note overlapping 0.12". check-layout reported 100/100 and no issues,
/// because text was only ever compared with nodes and containers, never with other text.
/// </summary>
public sealed class TextBlockOverlapTests
{
    private static Diagram Two(double y1, double y2)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 12 },
            TextBlocks =
            {
                new TextBlock { Id = "title", Text = "Title", Role = "title", Bounds = new Bounds(5, y1, 4, 0.6) },
                new TextBlock { Id = "subtitle", Text = "Sub", Role = "note", Bounds = new Bounds(5, y2, 4, 0.4) }
            }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void OverlappingTextBlocksAreReported()
    {
        var report = LayoutAnalyzer.Analyze(Two(9.25, 9.35));

        Assert.Contains(report.Issues, i => i.Code == "text.overlaps-text");
    }

    [Fact]
    public void TheReportNamesBothBlocks()
    {
        var issue = LayoutAnalyzer.Analyze(Two(9.25, 9.35)).Issues
            .First(i => i.Code == "text.overlaps-text");

        Assert.Contains("title", issue.Message);
        Assert.Contains("subtitle", issue.Message);
    }

    [Fact]
    public void WellSeparatedBlocksAreNotReported()
    {
        var report = LayoutAnalyzer.Analyze(Two(9.25, 7.25));

        Assert.DoesNotContain(report.Issues, i => i.Code == "text.overlaps-text");
    }

    [Fact]
    public void BlocksSideBySideDoNotOverlap()
    {
        // Same row, different columns: a legend beside a title is normal.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            TextBlocks =
            {
                new TextBlock { Id = "a", Text = "A", Bounds = new Bounds(3, 6, 2, 0.4) },
                new TextBlock { Id = "b", Text = "B", Bounds = new Bounds(8, 6, 2, 0.4) }
            }
        };

        Assert.DoesNotContain(
            LayoutAnalyzer.Analyze(new Diagram { Id = "t", Pages = { page } }).Issues,
            i => i.Code == "text.overlaps-text");
    }

    [Fact]
    public void EachPairIsReportedOnce()
    {
        var hits = LayoutAnalyzer.Analyze(Two(9.25, 9.35)).Issues
            .Count(i => i.Code == "text.overlaps-text");

        Assert.Equal(1, hits);
    }
}
