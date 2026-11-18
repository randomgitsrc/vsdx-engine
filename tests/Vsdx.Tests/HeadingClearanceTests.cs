using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Members must sit clear of a container's heading, and every layout engine must agree on that.
///
/// The geometry is exact, which is why it was missed for so long: a derived container is
/// `memberHeight + ContainerHeading + ContainerPaddingBottom` tall, and its members begin exactly
/// `ContainerPaddingBottom` above its bottom edge. So the members' top edge lands on the heading's
/// bottom edge **by construction** — not by a placement mistake, but because the two formulas are the
/// same formula. Every check that compares bounding boxes sees no overlap.
///
/// Four earlier attempts changed where members were placed; all were undone by `DiagramLayout.Apply`
/// re-deriving the box afterwards. The fix has to be in the derivation, and every engine that advances
/// by an envelope has to account for it — `Layered`, `Flow`, `Swimlane` and the banded path.
/// </summary>
public sealed class HeadingClearanceTests
{
    private static Diagram Banded(string kind = "layer")
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 14 }
        };

        page.Groups.Add(new Group { Id = "g1", Name = "上层", Kind = kind, Order = 2, Members = { "a", "b" } });
        page.Groups.Add(new Group { Id = "g2", Name = "下层", Kind = kind, Order = 1, Members = { "c", "d" } });

        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "g1", Bounds = new Bounds(4, 8, 1.4, 0.55) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "g1", Bounds = new Bounds(7, 8, 1.4, 0.55) });
        page.Nodes.Add(new Node { Id = "c", Text = "C", GroupId = "g2", Bounds = new Bounds(4, 4, 1.4, 0.55) });
        page.Nodes.Add(new Node { Id = "d", Text = "D", GroupId = "g2", Bounds = new Bounds(7, 4, 1.4, 0.55) });

        page.Edges.Add(new Edge { Id = "e1", From = "a", To = "c" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    private static LayoutReport Place(Diagram diagram, LayoutStrategy strategy)
    {
        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, strategy);

        return LayoutAnalyzer.Analyze(diagram);
    }

    [Theory]
    [InlineData(LayoutStrategy.Layered)]
    [InlineData(LayoutStrategy.Flow)]
    [InlineData(LayoutStrategy.Grid)]
    [InlineData(LayoutStrategy.Tree)]
    public void NoEnginePutsMembersInTheHeading(LayoutStrategy strategy)
    {
        var report = Place(Banded(), strategy);

        var collisions = report.Issues.Where(i => i.Code == "container.heading-collision").ToList();

        Assert.True(collisions.Count == 0,
            $"{strategy}: " + string.Join("; ", collisions.Select(c => c.Message)));
    }

    [Fact]
    public void SwimlaneBehavesTheSameWay()
    {
        var report = Place(Banded("lane"), LayoutStrategy.Swimlane);

        Assert.DoesNotContain(report.Issues, i => i.Code == "container.heading-collision");
    }

    [Fact]
    public void TheContainersStillDoNotOverlap()
    {
        // The clearance must be paid for by the advance, not borrowed from the next band.
        var diagram = Banded();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);

        var groups = diagram.Pages[0].Groups.Where(g => g.Bounds is not null).ToList();

        for (int i = 0; i < groups.Count; i++)
        for (int j = i + 1; j < groups.Count; j++)
        {
            var a = groups[i].Bounds!;
            var b = groups[j].Bounds!;

            double overlap = Math.Min(a.Y + a.H / 2, b.Y + b.H / 2) - Math.Max(a.Y - a.H / 2, b.Y - b.H / 2);

            Assert.True(overlap <= 1e-6, $"{groups[i].Id} and {groups[j].Id} overlap by {overlap:F3}");
        }
    }

    [Fact]
    public void TheClearanceIsSmallEnoughNotToWasteSpace()
    {
        // A gap that grew the page noticeably would be trading one defect for another.
        Assert.InRange(DiagramLayout.HeadingClearance, 0.05, 0.25);
    }
}
