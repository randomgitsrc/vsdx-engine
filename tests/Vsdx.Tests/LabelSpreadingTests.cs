using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Overlapping edge labels should be separated automatically when a position can do it.
///
/// Once the protocol could carry a label position (C10/C11) and the analyser could see it (C9),
/// spreading became possible: nudge one of the colliding edges along its route and the labels part.
/// Measured on the cross-border diagram, 0.25/0.75 cleared both collisions that a fixed 0.5 produced.
///
/// Two limits are deliberate. Only labels that have **not** been positioned by the author move — an
/// explicit choice is a decision, not a default to correct. And the spread only runs when it
/// actually removes the overlap, with the result re-checked; a rule that moved labels and left them
/// overlapping would be worse than doing nothing.
/// </summary>
public sealed class LabelSpreadingTests
{
    private static Diagram Coincident(double? first = null, double? second = null)
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
    public void CoincidentLabelsAreSeparated()
    {
        var diagram = Coincident();

        int moved = DecorationLayout.SpreadLabels(diagram);

        Assert.True(moved > 0, "nothing was moved");
        Assert.DoesNotContain(LayoutAnalyzer.Analyze(diagram).Issues,
            i => i.Code == "label.overlaps-label");
    }

    [Fact]
    public void AnAuthorPositionedLabelIsLeftAlone()
    {
        // Both edges carry explicit positions; respect them even though they collide.
        var diagram = Coincident(first: 0.5, second: 0.5);

        DecorationLayout.SpreadLabels(diagram);

        Assert.Equal(0.5, diagram.Pages[0].Edges[0].LabelPosition);
        Assert.Equal(0.5, diagram.Pages[0].Edges[1].LabelPosition);
    }

    [Fact]
    public void OnlyTheUnpositionedOneMoves()
    {
        // One explicit, one default: the default is the one that gives way.
        var diagram = Coincident(first: 0.5, second: null);

        DecorationLayout.SpreadLabels(diagram);

        Assert.Equal(0.5, diagram.Pages[0].Edges[0].LabelPosition);
        Assert.NotEqual(0.5, diagram.Pages[0].Edges[1].LabelPosition);
    }

    [Fact]
    public void LabelsThatDoNotCollideAreNotTouched()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 2, 1.4, 0.7) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(10, 2, 1.4, 0.7) },
                new Node { Id = "c", Text = "C", Bounds = new Bounds(2, 6.5, 1.4, 0.7) },
                new Node { Id = "d", Text = "D", Bounds = new Bounds(10, 6.5, 1.4, 0.7) }
            },
            Edges =
            {
                new Edge { Id = "e1", From = "a", To = "b", Label = "low" },
                new Edge { Id = "e2", From = "c", To = "d", Label = "high" }
            }
        };
        var diagram = new Diagram { Id = "t", Pages = { page } };

        Assert.Equal(0, DecorationLayout.SpreadLabels(diagram));
    }

    [Fact]
    public void TheResultIsRecheckedAfterMoving()
    {
        // The point of the operation is a clean diagram, not a moved field.
        var diagram = Coincident();

        DecorationLayout.SpreadLabels(diagram);

        Assert.DoesNotContain(LayoutAnalyzer.Analyze(diagram).Issues,
            i => i.Code == "label.overlaps-label");
    }
}
