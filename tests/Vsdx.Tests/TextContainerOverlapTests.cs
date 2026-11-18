using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A label straddling a container border has to be visible to the analyser.
///
/// Measured on the cross-border order diagram: the title spanned y 8.20-8.80 while the top band
/// spanned 5.85-8.76, so the heading sat across the band's border and collided with the band's own
/// title. check-layout reported 91/100 and said nothing, because the analyser compared nodes with
/// nodes and groups with groups — text blocks were never compared with anything.
///
/// A label wholly inside a band is a design choice and stays silent; only a border-crossing one is
/// a defect.
/// </summary>
public sealed class TextContainerOverlapTests
{
    private static Diagram Page(TextBlock block, Group group)
    {
        group.Members.Add("a");

        return new Diagram
        {
            Id = "t",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 10, Height = 8 },
                    Nodes = { new Node { Id = "a", Text = "A", Bounds = new Bounds(5, 3, 1.5, 0.7) } },
                    Groups = { group },
                    TextBlocks = { block }
                }
            }
        };
    }

    [Fact]
    public void ALabelStraddlingTheBorderIsReported()
    {
        // Container 4..6 in y; label 5.6..6.4 — half in, half out.
        var diagram = Page(
            new TextBlock { Id = "title", Text = "Title", Bounds = new Bounds(5, 6.0, 4, 0.8) },
            new Group { Id = "band", Bounds = new Bounds(5, 5, 6, 2) });

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.Contains(report.Issues, i => i.Code == "text.overlaps-container");
    }

    [Fact]
    public void TheReportNamesBothParties()
    {
        var diagram = Page(
            new TextBlock { Id = "title", Text = "Title", Bounds = new Bounds(5, 6.0, 4, 0.8) },
            new Group { Id = "band", Bounds = new Bounds(5, 5, 6, 2) });

        var issue = LayoutAnalyzer.Analyze(diagram).Issues
            .First(i => i.Code == "text.overlaps-container");

        Assert.Contains("title", issue.Message);
        Assert.Contains("band", issue.Message);
    }

    [Fact]
    public void ALabelEntirelyInsideABandIsNotReported()
    {
        // Zone headings live inside their band; flagging those would make the check useless.
        var diagram = Page(
            new TextBlock { Id = "zone", Text = "Zone", Bounds = new Bounds(5, 5, 3, 0.4) },
            new Group { Id = "band", Bounds = new Bounds(5, 5, 6, 2) });

        Assert.DoesNotContain(LayoutAnalyzer.Analyze(diagram).Issues,
            i => i.Code == "text.overlaps-container");
    }

    [Fact]
    public void ALabelEntirelyOutsideIsNotReported()
    {
        var diagram = Page(
            new TextBlock { Id = "note", Text = "Note", Bounds = new Bounds(5, 7.5, 3, 0.3) },
            new Group { Id = "band", Bounds = new Bounds(5, 5, 6, 2) });

        Assert.DoesNotContain(LayoutAnalyzer.Analyze(diagram).Issues,
            i => i.Code == "text.overlaps-container");
    }

    [Fact]
    public void EveryStraddledBandIsReported()
    {
        // A tall title can cross more than one band; each is a separate collision.
        var diagram = Page(
            new TextBlock { Id = "big", Text = "Big", Bounds = new Bounds(5, 5.5, 4, 3) },
            new Group { Id = "lower", Bounds = new Bounds(5, 4.2, 6, 1.6) });

        diagram.Pages[0].Groups.Add(new Group { Id = "upper", Bounds = new Bounds(5, 6.8, 6, 1.6) });

        var hits = LayoutAnalyzer.Analyze(diagram).Issues
            .Where(i => i.Code == "text.overlaps-container").ToList();

        Assert.Equal(2, hits.Count);
    }
}
