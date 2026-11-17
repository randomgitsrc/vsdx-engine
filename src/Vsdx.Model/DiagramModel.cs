namespace Vsdx.Model;

/// <summary>
/// JSON diagram model version. Bumped only for breaking changes.
/// </summary>
public static class ModelVersion
{
    public const string Current = "1.0";
}

/// <summary>Root document: one VSDX package, one or more pages.</summary>
public sealed class Diagram
{
    /// <summary>Stable diagram identifier (not a Visio identifier).</summary>
    public string Id { get; set; } = "diagram";
    public string? Name { get; set; }
    /// <summary>Diagram family hint: architecture, microservices, workflow, network, generic.</summary>
    public string? Type { get; set; }
    public PageSize? PageSize { get; set; }
    /// <summary>portrait | landscape</summary>
    public string? Orientation { get; set; }
    /// <summary>Page background colour (#RRGGBB) or "transparent".</summary>
    public string? Background { get; set; }
    /// <summary>JSON model version this document was written with.</summary>
    public string ModelVersion { get; set; } = Vsdx.Model.ModelVersion.Current;
    /// <summary>Render/authoring hints that do not change topology.</summary>
    public DiagramStyle? Style { get; set; }
    public List<Page> Pages { get; set; } = new();
    public Metadata Metadata { get; set; } = new();
}

public sealed class PageSize
{
    public double Width { get; set; } = 11.0;
    public double Height { get; set; } = 8.5;
    /// <summary>inches | cm | mm</summary>
    public string Unit { get; set; } = "inches";
}

public sealed class Page
{
    public string Id { get; set; } = "page-1";
    public string Name { get; set; } = "Page-1";
    public string? NameU { get; set; }
    /// <summary>Visio page ID as stored in the package (0-based). Null on authoring.</summary>
    public int? VisioId { get; set; }
    public PageSize? Size { get; set; }
    public string? Background { get; set; }
    public List<Group> Groups { get; set; } = new();
    public List<Node> Nodes { get; set; } = new();
    public List<Edge> Edges { get; set; } = new();
    public List<Layer> Layers { get; set; } = new();
    /// <summary>Objects that could not be mapped into the standard model.</summary>
    public List<UnmappedObject> Unmapped { get; set; } = new();
    /// <summary>Original ordering of Visio shape IDs, so round-trips can preserve z-order.</summary>
    public List<string>? ShapeOrder { get; set; }
    /// <summary>Free-form geometry that is not one of the built-in masters.</summary>
    public List<FreePath> Paths { get; set; } = new();
    /// <summary>Standalone text that is not attached to a node: titles, captions, legends.</summary>
    public List<TextBlock> TextBlocks { get; set; } = new();
}

/// <summary>
/// Text that exists on its own rather than as a node's label.
///
/// A diagram needs a title, an optional subtitle, footnotes and a legend. None of those are
/// nodes: they do not participate in layout, have no edges, and are positioned against the page
/// rather than against peers. Expressing them as zero-edge nodes would corrupt every layout and
/// quality metric, so they are a separate collection.
/// </summary>
public sealed class TextBlock
{
    public string Id { get; set; } = "text";
    public string? Name { get; set; }

    /// <summary>The text. Newlines are honoured.</summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Semantic role, which drives the default size: title | subtitle | caption | legend | note.
    /// An explicit <see cref="ShapeStyle.FontSize"/> overrides it.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>Visio shape ID in the package.</summary>
    public string? VisioShapeId { get; set; }

    /// <summary>Position and box in inches. A text block is not resized from its content.</summary>
    public Bounds Bounds { get; set; } = new(0, 0, 4, 0.5);

    public ShapeStyle? Style { get; set; }

    /// <summary>Horizontal alignment: left | center | right.</summary>
    public string? Align { get; set; }

    public Dictionary<string, string>? ShapeData { get; set; }
    public Dictionary<string, string>? NativeCells { get; set; }
    /// <summary>True when the block came from an external file rather than being authored.</summary>
    public bool? Imported { get; set; }
}

/// <summary>
/// A shape whose outline is carried by explicit geometry rather than a master.
///
/// Phase 8 (P1): such shapes were read back as `Node { Shape = "Rectangle" }`, because the reader
/// had nowhere to put the geometry. That silently changed a custom outline into a rectangle.
///
/// The raw geometry rows are kept verbatim. Rows the engine does not interpret are still stored
/// and written back unchanged, so an unsupported shape survives a round-trip instead of being
/// flattened — a requirement the reviewer stated explicitly.
/// </summary>
public sealed class FreePath
{
    public string Id { get; set; } = "path";
    public string? Name { get; set; }
    public string? NameU { get; set; }
    /// <summary>Visio shape ID in the package.</summary>
    public string? VisioShapeId { get; set; }

    /// <summary>Shape pin (centre) in inches; Visio Y grows upward.</summary>
    public Bounds Bounds { get; set; } = new(0, 0, 1, 1);

    /// <summary>Text on the shape, if any.</summary>
    public string? Text { get; set; }

    public ShapeStyle? Style { get; set; }
    public double? Angle { get; set; }

    /// <summary>
    /// Geometry sections, in the order they appear in the package. Each is a list of raw rows.
    /// The engine understands MoveTo/LineTo; anything else is preserved as-is.
    /// </summary>
    public List<PathGeometry> Geometry { get; set; } = new();

    public Dictionary<string, string>? ShapeData { get; set; }
    public List<string>? Layers { get; set; }
    public Dictionary<string, string>? NativeCells { get; set; }
}

/// <summary>One Geometry section: an ordered list of path rows.</summary>
public sealed class PathGeometry
{
    /// <summary>Visio section index (the <c>IX</c> attribute).</summary>
    public int Index { get; set; }

    /// <summary>NoFill / NoLine / NoShow etc., preserved verbatim.</summary>
    public Dictionary<string, string>? SectionCells { get; set; }

    public List<PathRow> Rows { get; set; } = new();
}

/// <summary>
/// One geometry row. <see cref="Type"/> is the Visio row type (MoveTo, LineTo, ArcTo, EllipticalArcTo,
/// SplineKnot, PolylineTo, ...). Rows of types the engine does not interpret are still round-tripped.
/// </summary>
public sealed class PathRow
{
    public string Type { get; set; } = "LineTo";

    /// <summary>Cell values keyed by cell name (X, Y, A, B, C, D, ...).</summary>
    public Dictionary<string, string> Cells { get; set; } = new();
}

public sealed class Group
{
    public string Id { get; set; } = "grp";
    public string? Name { get; set; }
    public string? NameU { get; set; }
    /// <summary>Member node IDs. Kept in sync with Node.GroupId.</summary>
    public List<string> Members { get; set; } = new();
    /// <summary>Parent group ID for nested grouping.</summary>
    public string? ParentId { get; set; }
    /// <summary>Visio shape ID of the container/group shape.</summary>
    public string? VisioShapeId { get; set; }
    public Bounds? Bounds { get; set; }
    public ShapeStyle? Style { get; set; }
    /// <summary>container | group</summary>
    public string? Kind { get; set; }
    public bool? AutoSize { get; set; }
    /// <summary>Layer names this group belongs to.</summary>
    public List<string>? Layers { get; set; }
}

public sealed class Node
{
    /// <summary>Stable, agent-facing identifier. Preserved across edits.</summary>
    public string Id { get; set; } = "node";
    public string? Name { get; set; }
    /// <summary>Semantic type: service, database, gateway, queue, user, ... </summary>
    public string? Type { get; set; }
    public string? Text { get; set; }
    /// <summary>Visio master / shape kind: Rectangle, Process, Circle, Diamond, ... </summary>
    public string? Shape { get; set; }
    /// <summary>Visio shape ID inside the page (numeric string). Preserved for round-trip.</summary>
    public string? VisioShapeId { get; set; }
    public Bounds Bounds { get; set; } = new();
    public string? GroupId { get; set; }
    public ShapeStyle? Style { get; set; }
    public Dictionary<string, string>? ShapeData { get; set; }
    /// <summary>Shape Data labels, keyed by row name.</summary>
    public Dictionary<string, string>? ShapeDataLabels { get; set; }
    public List<string>? Layers { get; set; }
    public string? Hyperlink { get; set; }
    /// <summary>Native Visio cells preserved verbatim when the model cannot express them.</summary>
    public Dictionary<string, string>? NativeCells { get; set; }
    public double? Angle { get; set; }
    /// <summary>True when the node came from an external file and was not created by this engine.</summary>
    public bool? Imported { get; set; }

    /// <summary>
    /// The package's original <c>Master</c> reference, verbatim, when the node came from an
    /// external file. Preserved so a round-trip does not silently drop the link to a master the
    /// engine does not recognise: the shape would survive but stop being a master instance.
    /// </summary>
    public string? SourceMaster { get; set; }
}

public sealed class Edge
{
    public string Id { get; set; } = "edge";
    public string? From { get; set; }
    public string? To { get; set; }
    /// <summary>left|right|top|bottom|auto</summary>
    public string? FromPort { get; set; }
    public string? ToPort { get; set; }
    /// <summary>straight|rightAngle|curved|dynamic</summary>
    public string? Kind { get; set; }
    public string? Label { get; set; }
    public string? LineColor { get; set; }
    public double? LineWeight { get; set; }
    /// <summary>Visio pattern index: 0=none, 1=solid, 2=dashed, ...</summary>
    public int? LinePattern { get; set; }
    /// <summary>none|arrow|triangle</summary>
    public string? BeginArrow { get; set; }
    public string? EndArrow { get; set; }
    /// <summary>Free-standing endpoints when the edge is not attached to nodes.</summary>
    public Point? FromPoint { get; set; }
    public Point? ToPoint { get; set; }
    public List<Point>? Waypoints { get; set; }
    /// <summary>Visio connector shape ID.</summary>
    public string? VisioShapeId { get; set; }
    public string? GroupId { get; set; }
    public List<string>? Layers { get; set; }
    public Dictionary<string, string>? ShapeData { get; set; }
    public string? LabelFont { get; set; }
    public string? LabelColor { get; set; }
    public double? LabelFontSize { get; set; }
    /// <summary>Position of the label along the connector path, 0..1.</summary>
    public double? LabelPosition { get; set; }
}

public sealed class Layer
{
    public string Id { get; set; } = "layer";
    public string Name { get; set; } = "Layer";
    public string? NameU { get; set; }
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool Print { get; set; } = true;
    /// <summary>Node/edge/group IDs assigned to this layer.</summary>
    public List<string> Members { get; set; } = new();
}

public sealed class Bounds
{
    /// <summary>Centre X in page units (inches by default).</summary>
    public double X { get; set; }
    /// <summary>Centre Y in page units (inches by default).</summary>
    public double Y { get; set; }
    public double W { get; set; } = 1.5;
    public double H { get; set; } = 0.7;

    public Bounds() { }
    public Bounds(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
    public Bounds Clone() => new(X, Y, W, H);
}

public sealed class Point
{
    public double X { get; set; }
    public double Y { get; set; }
    public Point() { }
    public Point(double x, double y) { X = x; Y = y; }
}

public sealed class ShapeStyle
{
    public string? Fill { get; set; }
    public string? FillPattern { get; set; }
    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }
    public int? StrokePattern { get; set; }
    public string? TextColor { get; set; }
    public string? Font { get; set; }
    public double? FontSize { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public string? HAlign { get; set; }
    public string? VAlign { get; set; }
    public double? Opacity { get; set; }
    public bool? Shadow { get; set; }
    public double? Radius { get; set; }
    public string? LineJoin { get; set; }
    /// <summary>Horizontal container padding override (inches).</summary>
    public double? PaddingX { get; set; }
    /// <summary>Container heading height override (inches).</summary>
    public double? HeadingHeight { get; set; }
    /// <summary>Container bottom padding override (inches).</summary>
    public double? PaddingBottom { get; set; }
}

public sealed class DiagramStyle
{
    public string? Font { get; set; }
    public double? FontSize { get; set; }
    /// <summary>Named palette applied when node styles omit colours.</summary>
    public string? Palette { get; set; }
    /// <summary>Grid/spacing used by autoroute helpers.</summary>
    public double? GridSize { get; set; }
    public bool? SnapToGrid { get; set; }
}

public sealed class Metadata
{
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;
    public string ModelVersion { get; set; } = Vsdx.Model.ModelVersion.Current;
    public string? SourceFile { get; set; }
    /// <summary>SHA-256 of the source VSDX, for incremental-change detection.</summary>
    public string? SourceHash { get; set; }
    public string? Backend { get; set; }
    public string? BackendVersion { get; set; }
    /// <summary>Tool and version that produced this model.</summary>
    public string? Generator { get; set; }
    public List<ChangeRecord> History { get; set; } = new();
    public Dictionary<string, string>? Extra { get; set; }
}

public sealed class ChangeRecord
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "modify";
    public string? Target { get; set; }
    public string? Detail { get; set; }
    public string? Tool { get; set; }
}

/// <summary>
/// A Visio object the adapter recognised structurally but could not represent in the
/// standard model. It is retained verbatim so saving does not silently drop it.
/// </summary>
public sealed class UnmappedObject
{
    public string VisioShapeId { get; set; } = "";
    public string? MasterNameU { get; set; }
    public string? Reason { get; set; }
    /// <summary>Raw Visio XML of the shape, captured for lossless re-emit.</summary>
    public string? RawXml { get; set; }
    public bool Preserved { get; set; }
}
