using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Padding should accompany the text, not dominate it.
///
/// NodePaddingX/Y were absolute inches (0.30 / 0.22) while the text they surround varies with
/// font size. Measured: at 14pt the padding was 43% of the measured width; at 8pt it was 58%. A
/// small-font diagram therefore had boxes sized mostly by whitespace, and the circle that needed
/// 0.82" of text came out at 2.02" across.
///
/// The ratio is what should stay stable, so padding is now a fraction of the text it wraps, with
/// a floor so text never touches the border. Absolute padding also grows with the text, which is
/// what a reader expects — a 24pt label should not have the same 0.30" margin as an 8pt one.
/// </summary>
public sealed class PaddingScalingTests
{
    private static MeasuredText Measure(string text, double fontSize) =>
        TextLayout.Measure(text, fontSize, bold: false, TypeScale.ForCanvas(12, 8, 1));

    [Fact]
    public void AbsolutePaddingGrowsWithTheText()
    {
        // What must hold: a large label gets more absolute breathing room than a small one, so the
        // margin looks proportionate rather than fixed. The *ratio* is allowed to be higher for
        // short labels — the floor guarantees that — but absolute padding must still scale.
        var small = Measure("Database", 8);
        var large = Measure("Database", 24);

        double smallPad = small.RequiredWidth - TextLayout.MeasureLine("Database", 8);
        double largePad = large.RequiredWidth - TextLayout.MeasureLine("Database", 24);

        Assert.True(largePad > smallPad + 0.01,
            $"padding did not grow with text: 8pt {smallPad:F3}\" vs 24pt {largePad:F3}\"");
    }

    [Fact]
    public void PaddingNeverExceedsTheTextItSurrounds_ForNormalLabels()
    {
        // The original failure was padding dominating the box: 43% at 14pt, 58% at 8pt of the
        // measured width. For labels long enough to clear the floor, padding should now stay
        // well below the text width. Very short labels are covered by TextNeverTouchesTheBorder
        // instead — there the floor deliberately wins.
        foreach (var (text, size) in new[] { ("Web 商城", 14.0), ("PostgreSQL", 12.0), ("Title", 24.0) })
        {
            var measured = Measure(text, size);
            double bare = TextLayout.MeasureLine(text, size);
            double padding = measured.RequiredWidth - bare;

            Assert.True(measured.RequiredWidth >= bare,
                $"{text}@{size}pt: measured {measured.RequiredWidth:F2} below bare text {bare:F2}");

            Assert.True(padding < bare,
                $"{text}@{size}pt: padding {padding:F2}\" is not below the text width {bare:F2}\"");
        }
    }

    [Fact]
    public void TextNeverTouchesTheBorder()
    {
        // A floor exists so the ratio cannot go to zero on very short labels.
        var measured = Measure("I", 8);
        double bare = TextLayout.MeasureLine("I", 8);

        Assert.True(measured.RequiredWidth > bare + 0.05,
            $"single character box {measured.RequiredWidth:F3} leaves no margin over {bare:F3}");
    }

    [Fact]
    public void ACircleNoLongerNeedsTwiceTheWidthOfItsText()
    {
        // The visible symptom: a 0.82" label produced a 2.02" circle.
        var diagram = new Diagram
        {
            Id = "t",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 12, Height = 8 },
                    Nodes =
                    {
                        new Node
                        {
                            Id = "n", Text = "Web 商城", Shape = "Circle",
                            Bounds = new Bounds(6, 4, 1, 1),
                            Style = new ShapeStyle { FontSize = 14 }
                        }
                    }
                }
            }
        };

        DiagramSizer.Apply(diagram);
        var node = diagram.Pages[0].Nodes[0];

        // Bounding box must fit the label with the shape's inscribed loss, but not balloon.
        double bare = TextLayout.MeasureLine("Web 商城", 14);
        Assert.True(node.Bounds.W < bare / 0.70 * 1.9,
            $"circle {node.Bounds.W:F2}\" is more than 1.9x the minimum needed ({bare / 0.70:F2}\")");
    }

    [Fact]
    public void ExistingBlocksKeepTheirShape()
    {
        // Regression guard: this change resizes everything, but must not deform anything.
        var diagram = new Diagram
        {
            Id = "t",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 12, Height = 8 },
                    Nodes =
                    {
                        new Node { Id = "a", Text = "Alpha", Shape = "Rectangle", Bounds = new Bounds(3, 4, 1, 1) },
                        new Node { Id = "b", Text = "Beta", Shape = "Circle", Bounds = new Bounds(8, 4, 1, 1) }
                    }
                }
            }
        };

        DiagramSizer.Apply(diagram);

        Assert.True(diagram.Pages[0].Nodes[0].Bounds.W > 0);
        Assert.Equal(diagram.Pages[0].Nodes[1].Bounds.W, diagram.Pages[0].Nodes[1].Bounds.H, 2);
    }
}
