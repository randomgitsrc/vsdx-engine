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

    /// <summary>
    /// Where a connector leaving <paramref name="box"/> in direction (dx, dy) should start or end.
    ///
    /// On a rectangle that is the box edge. On any other master the ink is inset — a parallelogram's top edge is
    /// narrower than its box, a diamond's corner is a point — so the box edge is empty space. `BoundaryPoint` used
    /// the box for every shape, which is why a connector to Kafka stopped 0.1" short of the trapezoid.
    ///
    /// The inset is the one <see cref="InscribedFactor"/> already describes for text, because the same geometry
    /// governs both: how much of the box the glyph actually occupies.
    /// </summary>
    public static Model.Point OutlinePoint(Model.Bounds box, string? shape, double dx, double dy)
    {
        var factor = OutlineFactor(shape);

        // A circle keeps its full radius because it is drawn square already; its edge is the box edge.
        if (factor >= 1.0 || IsSquareShape(shape) && factor >= 0.99)
            return BoundaryOnBox(box, dx, dy);

        // Pull the hit point toward the centre by the inset, then re-project onto the box along the same direction
        // so the point stays on the boundary of the inset shape rather than inside it.
        var centre = new Model.Point(box.X, box.Y);
        var onBox = BoundaryOnBox(box, dx, dy);

        double px = centre.X + (onBox.X - centre.X) * factor;
        double py = centre.Y + (onBox.Y - centre.Y) * factor;

        return new Model.Point(px, py);
    }

    /// <summary>
    /// How much of the bounding box a shape's ink fills along its edges.
    ///
    /// Distinct from <see cref="InscribedFactor"/> for squares: a circle inscribed in its square reaches the box
    /// edge at the cardinal points, even though its text area is only 0.70 of the diameter.
    /// </summary>
    public static double OutlineFactor(string? shape)
    {
        if (string.IsNullOrWhiteSpace(shape)) return 1.0;

        return shape.Trim().ToLowerInvariant() switch
        {
            // Round shapes touch the box at the four cardinal points.
            "circle" or "ellipse" => 1.0,

            // Opposite corners joined: the ink reaches the box only at the edge midpoints.
            "diamond" or "decision" => 0.5,

            "hexagon" => 0.75,
            "parallelogram" or "data" => 0.75,
            "trapezoid" => 0.75,
            "pentagon" => 0.75,

            // A cylinder fills its box horizontally.
            "database" or "cylinder" or "queue" => 1.0,

            _ => 1.0
        };
    }

    /// <summary>The point where direction (dx, dy) from the centre leaves the rectangle.</summary>
    private static Model.Point BoundaryOnBox(Model.Bounds b, double dx, double dy)
    {
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return new Model.Point(b.X, b.Y);

        double halfW = b.W / 2, halfH = b.H / 2;
        double scaleX = Math.Abs(dx) < 1e-9 ? double.PositiveInfinity : halfW / Math.Abs(dx);
        double scaleY = Math.Abs(dy) < 1e-9 ? double.PositiveInfinity : halfH / Math.Abs(dy);
        double scale = Math.Min(scaleX, scaleY);

        return new Model.Point(b.X + dx * scale, b.Y + dy * scale);
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
