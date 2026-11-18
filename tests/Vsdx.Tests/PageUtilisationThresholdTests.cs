using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `page.underused` has two thresholds and both do what they say.
///
/// `MinPageUtilisation = 0.30` — below this the page is reported at all.
/// `SeverelyUnderusedRatio = 0.15` — below this it escalates from warning to error, on the reasoning that
/// a page barely larger than its content is a style question while a mostly blank one reads as broken.
///
/// Measured across page sizes with the same two-node drawing: 3x3 gives 21% utilisation and a warning,
/// 4x4 gives 12% and an error, and 2x2 reports nothing because the content fills it. The middle band is
/// the one worth pinning — a threshold pair is easy to get backwards and neither end would notice.
/// </summary>
public sealed class PageUtilisationThresholdTests
{
    private static Diagram TwoNodes() => new()
    {
        Id = "t",
        Pages =
        {
            new Page
            {
                Name = "p",
                Size = new PageSize { Width = 10, Height = 10 },
                Nodes =
                {
                    new Node { Id = "a", Text = "A", Bounds = new Bounds(4, 6, 1.4, 0.7) },
                    new Node { Id = "b", Text = "B", Bounds = new Bounds(6, 6, 1.4, 0.7) }
                },
                Edges = { new Edge { Id = "e", From = "a", To = "b" } }
            }
        }
    };

    private static LayoutIssue? Underused(double pageSide)
    {
        var diagram = TwoNodes();
        diagram.Pages[0].Size = new PageSize { Width = pageSide, Height = pageSide };

        return LayoutAnalyzer.Analyze(diagram).Issues.FirstOrDefault(i => i.Code == "page.underused");
    }

    [Fact]
    public void ABarelyLargerPageIsAWarning()
    {
        var issue = Underused(3.0);

        Assert.NotNull(issue);
        Assert.Equal("warning", issue!.Severity);
    }

    [Fact]
    public void AMostlyEmptyPageIsAnError()
    {
        var issue = Underused(4.0);

        Assert.NotNull(issue);
        Assert.Equal("error", issue!.Severity);
    }

    [Fact]
    public void AFittingPageIsNotReported()
    {
        Assert.Null(Underused(2.0));
    }

    [Fact]
    public void TheThresholdOrderIsTheSensibleOne()
    {
        // A severe threshold above the reporting threshold would make the error branch unreachable.
        var options = new LayoutAnalyzerOptions();

        Assert.True(options.SeverelyUnderusedRatio < options.MinPageUtilisation,
            $"severity threshold {options.SeverelyUnderusedRatio} is not below report threshold {options.MinPageUtilisation}");
    }

    [Fact]
    public void AnErrorLevelUnderusedPageZeroesTheScore()
    {
        // The consequence a caller sees: an overwhelmingly blank page is not "88 with a note".
        var diagram = TwoNodes();
        diagram.Pages[0].Size = new PageSize { Width = 8, Height = 8 };

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.False(report.HardConstraintsPassed);
        Assert.Equal(0, report.Score);
    }
}
