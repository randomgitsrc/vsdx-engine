using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A container's heading needs clear space; a member sitting against the top edge collides with it.
///
/// Observed on the cross-border e-commerce diagram at score 98/100: the "数据层 Data" heading was
/// painted over by the PostgreSQL box, and **no detector reported it**. Measuring the model showed why
/// the existing checks miss it — the member's top edge sat at exactly the heading's bottom edge
/// (2.55 both), so no bounding box overlapped and `node.overlap` and `text.overlaps-node` both had
/// nothing to fire on. The heading occupies the top `ContainerHeading` inches of a container's box, and
/// members must stay below that band, not just inside the box.
/// </summary>
public sealed class ContainerHeadingClearanceTests
{
    /// <summary>Must match DiagramLayout.ContainerHeading.</summary>
    private const double Heading = 0.46;

    private static Diagram Banded(double memberTopGap)
    {
        // A container whose top is at y=3.01, with members placed at a chosen clearance below it.
        var containerTop = 3.01;
        var containerCenter = containerTop - 0.615;

        var memberCenter = containerTop - Heading - 0.275 - memberTopGap;

        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Groups =
            {
                // `Members` is what the containment check iterates; setting only Node.GroupId leaves
                // the loop with nothing to walk. Same fixture mistake as the swimlane test.
                new Group
                {
                    Id = "g", Name = "数据层", Kind = "layer",
                    Members = { "a", "b" },
                    Bounds = new Bounds(5, containerCenter, 8, 1.23)
                }
            },
            Nodes =
            {
                new Node { Id = "a", Text = "A", GroupId = "g", Bounds = new Bounds(3, memberCenter, 1.4, 0.55) },
                new Node { Id = "b", Text = "B", GroupId = "g", Bounds = new Bounds(7, memberCenter, 1.4, 0.55) }
            }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void AStandardLayoutLeavesTheHeadingClear()
    {
        var diagram = Banded(memberTopGap: 0.05);
        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code == "container.heading-collision");
    }

    [Fact]
    public void AMemberAgainstTheHeadingIsReported()
    {
        // Zero clearance: the member's top edge is exactly at the heading's bottom edge. This is the
        // state the real diagram produced, and it produced no issue at all.
        var diagram = Banded(memberTopGap: 0);
        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.Contains(report.Issues, i => i.Code == "container.heading-collision");
    }

    [Fact]
    public void TheIssueNamesTheContainerAndTheMember()
    {
        var diagram = Banded(memberTopGap: 0);
        var issue = LayoutAnalyzer.Analyze(diagram).Issues
            .FirstOrDefault(i => i.Code == "container.heading-collision");

        Assert.NotNull(issue);
        Assert.Contains("g", issue!.Targets);
        Assert.Contains("a", issue.Targets);
    }

    [Fact]
    public void TheHeadingBandIsTheOneTheLayoutActuallyReserves()
    {
        // If DiagramLayout changes its reserve, this test's constant is wrong and should be updated
        // with it — the point is that the two cannot drift apart silently.
        Assert.Equal(Heading, DiagramLayout.ContainerHeading);
        Assert.True(DiagramLayout.ContainerHeading > 0);
    }
}
