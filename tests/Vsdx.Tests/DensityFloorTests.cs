using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The "type has hit its floor, split the diagram" warning cannot fire.
///
/// `DiagramSizer.Apply` guards it with `if (!scale.IsLegible)`, and `IsLegible` is
/// `NodeTitle >= MinReadablePt`. But the only way `scale` is produced in that path is
/// `TypeScale.ForCanvas`, which computes `body = Clamp(11 * sizeFactor * densityFactor, MinReadablePt, MaxBodyPt)`
/// — the floor is baked into the clamp, so `NodeTitle` is never below it and the guard is never true.
///
/// The consequence is the one this project treats as a rule: a diagram too dense to read at a legible size
/// gets no report. It just renders with everything at 8pt, however many nodes were packed in, and the caller
/// sees a normal result.
/// </summary>
public sealed class DensityFloorTests
{
    private static Diagram Packed(int nodes, double width = 11.69, double height = 8.27)
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = width, Height = height } };

        for (int i = 0; i < nodes; i++)
            page.Nodes.Add(new Node
            {
                Id = $"n{i}",
                Text = $"组件{i}",
                Bounds = new Bounds(1.0 + (i % 10) * 1.05, 1.0 + (i / 10) * 0.7, 1.3, 0.55)
            });

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void ForCanvasNeverReturnsIllegibleType()
    {
        // Even absurd density stays at the floor rather than below it.
        foreach (var count in new[] { 1, 12, 40, 200, 500 })
        {
            var scale = TypeScale.ForCanvas(11.69, 8.27, count);

            Assert.True(scale.IsLegible, $"{count} nodes produced {scale.NodeTitle:F1}pt");
            Assert.True(scale.NodeTitle >= TypeScale.MinReadablePt);
        }
    }

    [Fact]
    public void ThereforeTheFloorWarningIsUnreachable()
    {
        // The guard is `!IsLegible`, and IsLegible is always true on this path.
        var report = DiagramSizer.Apply(Packed(200));

        Assert.DoesNotContain(report.Issues, i => i.Contains("字号已触底", StringComparison.Ordinal));
    }

    [Fact]
    public void ADensePageIsAcceptedWithoutComplaint()
    {
        // 200 nodes on A4 landscape: nothing says this is too much for the sheet.
        var diagram = Packed(200);

        DiagramSizer.Apply(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code.Contains("text-too-small", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOverridePathIsTheOnlyOneThatCanTriggerIt()
    {
        // Proves the guard is not dead by construction — it is dead because the only producer clamps.
        var tiny = new TypeScale { NodeTitle = 5, NodeCaption = 5, RegionTitle = 6, EdgeLabel = 5, Annotation = 5 };

        var report = DiagramSizer.Apply(Packed(6), tiny);

        Assert.Contains(report.Issues, i => i.Contains("字号已触底", StringComparison.Ordinal));
    }
}
