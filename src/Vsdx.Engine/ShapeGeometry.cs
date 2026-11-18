namespace Vsdx.Engine;

/// <summary>
/// How much of a shape's bounding box is actually usable for text.
///
/// Sizing measured every node as a rectangle, so shapes whose drawable area is smaller than their
/// bounds — circles, diamonds — clipped their labels even though the same text fitted a rectangle
/// of the same size. The figures here are geometric, not aesthetic:
///
///   circle    inscribed square is r*sqrt(2) per side, ~71% of the diameter
///   diamond   a rectangle inside satisfies w+h &lt;= diagonal, so roughly half
///   hexagon   between the two
///
/// Unknown shapes get 1.0. Assuming a constraint a custom master may not have would inflate every
/// unrecognised shape, and a too-large box is a cosmetic problem while a too-small one loses text.
/// </summary>
public static class ShapeGeometry
{
    /// <summary>
    /// Fraction of the bounding box available for text, in the range (0, 1].
    /// </summary>
    public static double InscribedFactor(string? shape)
    {
        if (string.IsNullOrWhiteSpace(shape)) return 1.0;

        return shape.Trim().ToLowerInvariant() switch
        {
            // A circle's inscribed square: 0.707 of the diameter. Slightly conservative because
            // real glyph boxes are taller than their cap height.
            "circle" or "ellipse" => 0.70,

            // A rectangle inside a diamond must satisfy w + h <= diagonal; for square-ish label
            // areas that leaves about half the width.
            "diamond" or "decision" => 0.50,

            // Six sides: wider than a diamond, narrower than a rectangle.
            "hexagon" => 0.75,

            // Text sits in the upper portion of a cylinder/queue glyph.
            "database" or "cylinder" or "queue" => 0.85,

            _ => 1.0
        };
    }

    /// <summary>True for shapes that must be drawn from a square box to look like themselves.</summary>
    public static bool IsSquareShape(string? shape) =>
        shape?.Trim().ToLowerInvariant() is "circle" or "ellipse" or "diamond" or "decision";

    /// <summary>
    /// The multiplier applied to a measured text box to obtain the shape's bounding box.
    ///
    /// The inverse of <see cref="InscribedFactor"/>: to fit a width w inside a circle, the circle
    /// must be w / 0.70 wide.
    /// </summary>
    public static double PaddingFactor(string? shape)
    {
        double factor = InscribedFactor(shape);
        return factor <= 0 || factor > 1 ? 1.0 : 1.0 / factor;
    }
}
