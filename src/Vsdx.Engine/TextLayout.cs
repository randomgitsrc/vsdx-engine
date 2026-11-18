using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// The font scale for one diagram. These are ratios and bounds, not magic numbers scattered
/// through the layout code: every consumer asks the scale instead of hard-coding 11pt.
/// </summary>
public sealed class TypeScale
{
    /// <summary>Smallest size that stays legible in print and on screen at 100%.</summary>
    public const double MinReadablePt = 8.0;

    /// <summary>Largest body size; beyond this a diagram reads as a poster, not a figure.</summary>
    public const double MaxBodyPt = 14.0;

    public double DiagramTitle { get; init; } = 20;
    public double RegionTitle { get; init; } = 12;
    public double NodeTitle { get; init; } = 11;
    public double NodeCaption { get; init; } = 10;
    public double EdgeLabel { get; init; } = 9;
    public double Annotation { get; init; } = 9;

    /// <summary>Line height as a multiple of the font size. 1.28 keeps two-line labels breathable.</summary>
    public double LineHeightFactor { get; init; } = 1.28;

    /// <summary>
    /// Horizontal padding as a fraction of the text width, with <see cref="MinNodePaddingX"/> as a
    /// floor.
    ///
    /// It used to be a flat 0.30in, which does not work across font sizes: at 14pt that is 43% of
    /// the label width and at 8pt it is 58%, so small-font diagrams were sized mostly by
    /// whitespace. A ratio keeps the visual breathing room constant instead.
    /// </summary>
    public double NodePaddingRatio { get; init; } = 0.20;

    /// <summary>Smallest horizontal padding, so short labels never touch the border.</summary>
    public double MinNodePaddingX { get; init; } = 0.15;

    /// <summary>Vertical padding as a fraction of the line height, floored like the horizontal one.</summary>
    public double NodePaddingRatioY { get; init; } = 0.22;

    /// <summary>Smallest vertical padding.</summary>
    public double MinNodePaddingY { get; init; } = 0.11;

    /// <summary>
    /// Derives a scale from canvas size and node count. Larger canvases and sparser diagrams
    /// can afford larger type; dense diagrams shrink toward the readable floor but never
    /// below it — over-density must be solved by splitting the diagram, not by shrinking text.
    /// </summary>
    public static TypeScale ForCanvas(double widthIn, double heightIn, int nodeCount)
    {
        if (widthIn <= 0 || heightIn <= 0) return new TypeScale();

        // Normalise to a "unit" that grows with the canvas but is bounded, so a huge page does
        // not produce comic-book type.
        double diagonal = Math.Sqrt(widthIn * widthIn + heightIn * heightIn);
        double sizeFactor = Math.Clamp(diagonal / 20.0, 0.75, 1.5);

        // Density pressure: many nodes on a small page pushes type down, but only to the floor.
        double area = widthIn * heightIn;
        double density = nodeCount <= 0 ? 1.0 : area / Math.Max(nodeCount, 1);
        double densityFactor = Math.Clamp(Math.Sqrt(density / 8.0), 0.85, 1.3);

        double body = Math.Clamp(11.0 * sizeFactor * densityFactor, MinReadablePt, MaxBodyPt);

        return new TypeScale
        {
            NodeTitle = body,
            NodeCaption = Math.Max(MinReadablePt, body - 1.0),
            RegionTitle = Math.Max(MinReadablePt, body + 1.0),
            EdgeLabel = Math.Max(MinReadablePt - 1.0, body - 2.0),
            Annotation = Math.Max(MinReadablePt - 1.0, body - 2.0),
            DiagramTitle = Math.Clamp(body * 1.8, 14, 26)
        };
    }

    /// <summary>True when the scale is still above the legibility floor.</summary>
    public bool IsLegible => NodeTitle >= MinReadablePt;
}

/// <summary>
/// Text measurement used to *drive* geometry rather than to check it afterwards.
///
/// Phase 0 established that the existing estimator is accurate to roughly 3% on real fonts
/// (0% on CJK), so the problem was never precision — it was that nothing called it before
/// positioning shapes. This class is the entry point layout code uses.
/// </summary>
public static class TextLayout
{
    private const double DefaultFontSize = 11.0;
    private const double BoldWidthFactor = 1.06;

    /// <summary>
    /// Measures a multi-line label at a given size, returning the box it needs including the
    /// padding a node applies around its text.
    /// </summary>
    public static MeasuredText Measure(
        string? text, double fontSize, bool bold = false, TypeScale? scale = null)
    {
        scale ??= new TypeScale();
        if (string.IsNullOrEmpty(text))
        {
            return new MeasuredText(0, 0, 1, fontSize);
        }

        double widthFactor = bold ? BoldWidthFactor : 1.0;
        var lines = text.Replace("\r\n", "\n").Split('\n');

        double widest = 0;
        foreach (var line in lines)
            widest = Math.Max(widest, MeasureLine(line, fontSize, widthFactor));

        double lineHeight = fontSize / 72.0 * scale.LineHeightFactor;
        double textHeight = lines.Length * lineHeight;
        double textWidth = widest;

        // Padding accompanies the text rather than being fixed: a 24pt label should not share the
        // 0.30in margin of an 8pt one, and an 8pt one should not be mostly margin.
        double padX = Math.Max(scale.MinNodePaddingX, textWidth * scale.NodePaddingRatio);
        double padY = Math.Max(scale.MinNodePaddingY, textHeight * scale.NodePaddingRatioY);

        return new MeasuredText(
            textWidth + padX * 2,
            textHeight + padY * 2,
            lines.Length,
            fontSize);
    }

    /// <summary>Measures a single line of text in inches, honouring CJK width and bold spread.</summary>
    public static double MeasureLine(string line, double fontSize, double widthFactor = 1.0)
    {
        if (string.IsNullOrEmpty(line)) return 0;

        double widthPt = 0;
        foreach (var ch in line)
        {
            if (ch >= '\u2E80') widthPt += fontSize * 1.0;          // CJK: square glyphs
            else if (char.IsWhiteSpace(ch)) widthPt += fontSize * 0.28;
            else if ("iljtfrI.,:;'!|".Contains(ch)) widthPt += fontSize * 0.30;
            else if ("MWmw@%".Contains(ch)) widthPt += fontSize * 0.86;
            else if (char.IsUpper(ch)) widthPt += fontSize * 0.66;
            else widthPt += fontSize * 0.53;
        }
        return widthPt / 72.0 * widthFactor;
    }

    /// <summary>
    /// Wraps text to fit <paramref name="maxWidthIn"/> inches, preferring a break at the
    /// author's existing newlines and then at spaces; CJK may break between any two glyphs.
    /// Returns the wrapped text and the box it needs.
    /// </summary>
    public static (string text, MeasuredText measure) Wrap(
        string? text, double maxWidthIn, double fontSize, bool bold = false, TypeScale? scale = null)
    {
        scale ??= new TypeScale();
        if (string.IsNullOrWhiteSpace(text))
            return (text ?? "", Measure(text, fontSize, bold, scale));

        double usable = Math.Max(0.2, maxWidthIn - Math.Max(scale.MinNodePaddingX, maxWidthIn * scale.NodePaddingRatio / 2) * 2);
        double widthFactor = bold ? BoldWidthFactor : 1.0;
        var output = new List<string>();

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (MeasureLine(rawLine, fontSize, widthFactor) <= usable)
            {
                output.Add(rawLine);
                continue;
            }

            var current = new System.Text.StringBuilder();
            foreach (var token in Tokenize(rawLine))
            {
                var candidate = current.Length == 0 ? token : current + token;
                if (current.Length > 0 && MeasureLine(candidate, fontSize, widthFactor) > usable)
                {
                    output.Add(current.ToString().TrimEnd());
                    current.Clear();
                    current.Append(token.TrimStart());
                }
                else
                {
                    current.Clear();
                    current.Append(candidate);
                }
            }
            if (current.Length > 0) output.Add(current.ToString().TrimEnd());
        }

        var joined = string.Join("\n", output);
        return (joined, Measure(joined, fontSize, bold, scale));
    }

    /// <summary>
    /// Splits a line into break opportunities: spaces stay attached to the preceding word so a
    /// wrapped line does not start with a space, and each CJK glyph is its own token.
    /// </summary>
    private static IEnumerable<string> Tokenize(string line)
    {
        var buffer = new System.Text.StringBuilder();
        foreach (var ch in line)
        {
            bool cjk = ch >= '\u2E80';
            if (cjk)
            {
                if (buffer.Length > 0) { yield return buffer.ToString(); buffer.Clear(); }
                yield return ch.ToString();
            }
            else if (ch == ' ')
            {
                buffer.Append(ch);
                yield return buffer.ToString();
                buffer.Clear();
            }
            else
            {
                buffer.Append(ch);
            }
        }
        if (buffer.Length > 0) yield return buffer.ToString();
    }
}

/// <summary>The box a label needs, in inches, including padding.</summary>
public readonly record struct MeasuredText(
    double RequiredWidth, double RequiredHeight, int LineCount, double FontSize)
{
    public bool FitsWithin(double widthIn, double heightIn, double tolerance = 0.02) =>
        RequiredWidth <= widthIn + tolerance && RequiredHeight <= heightIn + tolerance;
}
