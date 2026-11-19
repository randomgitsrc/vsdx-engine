namespace Vsdx.Model;

/// <summary>
/// Where connectors may attach to a shape.
///
/// Visio connection points are fractions of the shape's bounding box, so a shape that is resized keeps its anchors
/// proportional. The masters here ship a single point at the box centre, which means every connector ending on a
/// node lands on the same coordinate — a node with three inbound edges gets a knot, and no routing can avoid it.
///
/// The points below put one anchor at the middle of each side, which is what the router already assumes when it
/// decides to leave via `left` or `top`. Shapes whose ink does not reach the box edge get points on their outline
/// instead, for the same reason `ShapeGeometry.OutlinePoint` exists.
/// </summary>
public static class ConnectionPointLayout
{
    /// <summary>One anchor, as a fraction of the shape's box, with the direction a connector should leave in.</summary>
    public readonly record struct Point(double X, double Y, double DirX, double DirY);

    /// <summary>
    /// Anchors for one node. Always four, ordered left, right, bottom, top.
    ///
    /// Four is the number the router can use: it names those four sides when choosing an exit, and a fifth point
    /// would be one no caller could ask for.
    /// </summary>
    public static IReadOnlyList<Point> For(Node node)
    {
        double inset = OutlineInset(node.Shape);

        // A diamond or hexagon meets its box at the middle of each side, not at the corners, so an inset of zero
        // already lands on the ink there. The inset matters for shapes whose edge is pulled inward — a diamond's
        // sides are diagonals between the vertices.
        double left = inset, right = 1 - inset;
        double bottom = inset, top = 1 - inset;

        return new[]
        {
            new Point(inset == 0 ? 0 : left, 0.5, -1, 0),
            new Point(inset == 0 ? 1 : right, 0.5, 1, 0),
            new Point(0.5, inset == 0 ? 0 : bottom, 0, -1),
            new Point(0.5, inset == 0 ? 1 : top, 0, 1)
        };
    }

    /// <summary>
    /// How far a side's midpoint sits inside the bounding box for this master, as a fraction of the side.
    ///
    /// Zero for anything whose ink reaches the box edge at the middle of a side, which is every master here except
    /// the ones drawn as diagonals.
    /// </summary>
    private static double OutlineInset(string? shape) => shape?.Trim().ToLowerInvariant() switch
    {
        // Vertices at the box edge midpoints: the sides are diagonals, and the midpoint of a side is inside.
        "diamond" or "decision" => 0.25,

        // Six sides: the flat parts reach the left and right edges, the top and bottom are shorter.
        "hexagon" => 0,

        _ => 0
    };
}
