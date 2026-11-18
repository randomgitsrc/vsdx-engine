using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Records why a sequence diagram cannot be built from the current model.
///
/// Attempted with `layout=manual` and authored coordinates — the last option that avoids writing a new
/// layout strategy. Participants place correctly (five actors across the top, coordinates preserved),
/// but every request/reply pair shares both endpoints, so their connectors coincide and their labels
/// share a midpoint.
///
/// Alternating `labelPosition` does not help: it moves a label along its own line, and the two lines
/// are the same line. Clearing this needs endpoints addressed by (participant column, message index),
/// which the model has no way to express.
///
/// These tests pin the current limitation so a future change to edges or labelling has a signal.
/// </summary>
public sealed class SequenceFeasibilityTests
{
    /// <summary>Five participants in a row, as a sequence diagram header would have them.</summary>
    private static Diagram Header()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 17, Height = 13 }
        };

        double[] xs = { 2, 5, 8, 11, 14 };
        string[] ids = { "user", "web", "order", "pay", "gw" };

        for (int i = 0; i < ids.Length; i++)
            page.Nodes.Add(new Node { Id = ids[i], Text = ids[i], Bounds = new Bounds(xs[i], 10, 1.4, 0.7) });

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void ARequestAndItsReplyShareAMidpoint()
    {
        var diagram = Header();
        var page = diagram.Pages[0];

        page.Edges.Add(new Edge { Id = "m1", From = "user", To = "web", Label = "1 submit" });
        page.Edges.Add(new Edge { Id = "m2", From = "web", To = "user", Label = "2 reply" });

        var report = LayoutAnalyzer.Analyze(diagram);

        // Both labels land on the same point: same endpoints, opposite direction.
        Assert.Contains(report.Issues, i => i.Code == "label.overlaps-label");
    }

    [Fact]
    public void AlternatingLabelPositionsDoNotSeparateThem()
    {
        // The line is the same line, so sliding along it cannot help — this is the observation that
        // rules out a workaround.
        var diagram = Header();
        var page = diagram.Pages[0];

        page.Edges.Add(new Edge { Id = "m1", From = "user", To = "web", Label = "1 submit", LabelPosition = 0.3 });
        page.Edges.Add(new Edge { Id = "m2", From = "web", To = "user", Label = "2 reply", LabelPosition = 0.7 });

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.Contains(report.Issues, i => i.Code == "label.overlaps-label");
    }

    [Fact]
    public void SeparatingTheParticipantsDoesNotHelp()
    {
        // Vertical separation is what a real sequence diagram relies on — each message at its own y.
        // The model cannot express it: an edge runs between two shapes, so its endpoints are their
        // centres, not a point along a lifeline.
        var diagram = Header();
        var page = diagram.Pages[0];

        page.Edges.Add(new Edge { Id = "m1", From = "user", To = "web", Label = "1 submit" });
        page.Edges.Add(new Edge { Id = "m2", From = "user", To = "web", Label = "2 again" });

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.Contains(report.Issues, i => i.Code == "label.overlaps-label");
    }

    [Fact]
    public void ASequenceShapedDiagramStillReportsNoGeometryFaultWhenLabelsAreOmitted()
    {
        // Structure alone is fine: five participants, eight connectors, nothing overlapping. The
        // problem is specifically the labels, which is why scores look healthy on such a diagram.
        var diagram = Header();
        var page = diagram.Pages[0];

        string[][] pairs =
        {
            new[] { "user", "web" }, new[] { "web", "order" }, new[] { "order", "pay" },
            new[] { "pay", "gw" }, new[] { "gw", "pay" }, new[] { "pay", "order" },
            new[] { "order", "web" }, new[] { "web", "user" }
        };

        for (int i = 0; i < pairs.Length; i++)
            page.Edges.Add(new Edge { Id = $"m{i + 1}", From = pairs[i][0], To = pairs[i][1] });

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code is "node.overlap" or "group.overlap");
    }
}
