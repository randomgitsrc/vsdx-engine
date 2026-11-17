using System.IO.Compression;
using System.Xml.Linq;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>
/// Compares the structure of a package before and after a save, and refuses to hand back an
/// output that lost content.
///
/// The accepted decision is unambiguous: a restricted external-edit capability is acceptable, but
/// damaging the original file is not, and the engine must never silently emit a VSDX that may have
/// hurt the original shapes. Refusing is the correct failure mode; producing a plausible-looking
/// but damaged file is the wrong one.
///
/// The check is structural, not visual: it compares shape ids, their cell sets and their geometry
/// row counts. That catches the class of defect the Phase 8 audit found (a synthesised rectangle
/// silently replacing a real outline) without needing to understand every feature.
/// </summary>
public static class FidelityGuard
{
    private static readonly XNamespace VisioNs = "http://schemas.microsoft.com/office/visio/2012/main";

    /// <summary>Structure fingerprint of one page part.</summary>
    public sealed record ShapeFingerprint(
        string Id, string? NameU, string? Master, int CellCount, int GeometryRows,
        string CellNames, string GeometrySignature);

    /// <summary>
    /// Captures the shape structure of a package, keyed by page index then shape id.
    /// </summary>
    public static Dictionary<int, Dictionary<string, ShapeFingerprint>> Capture(string vsdxPath)
    {
        var result = new Dictionary<int, Dictionary<string, ShapeFingerprint>>();

        using var zip = ZipFile.OpenRead(vsdxPath);
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)) continue;
            if (!entry.FullName.EndsWith(".xml", StringComparison.Ordinal)) continue;

            var name = Path.GetFileNameWithoutExtension(entry.FullName);
            if (!name.StartsWith("page", StringComparison.Ordinal)) continue;
            if (!int.TryParse(name[4..], out int oneBased)) continue;

            XDocument doc;
            using (var stream = entry.Open()) doc = XDocument.Load(stream);

            var shapes = new Dictionary<string, ShapeFingerprint>(StringComparer.Ordinal);
            foreach (var shape in doc.Descendants(VisioNs + "Shape"))
            {
                var id = (string?)shape.Attribute("ID");
                if (id is null) continue;

                var cells = shape.Elements(VisioNs + "Cell")
                    .Select(c => (string?)c.Attribute("N"))
                    .Where(n => n is not null)
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                var geometry = shape.Elements(VisioNs + "Section")
                    .Where(sec => (string?)sec.Attribute("N") == "Geometry")
                    .ToList();

                var signature = string.Join(";", geometry.Select(sec =>
                    string.Join(",", sec.Elements(VisioNs + "Row").Select(r => (string?)r.Attribute("T")))));

                shapes[id] = new ShapeFingerprint(
                    id,
                    (string?)shape.Attribute("NameU"),
                    (string?)shape.Attribute("Master"),
                    cells.Count,
                    geometry.Sum(sec => sec.Elements(VisioNs + "Row").Count()),
                    string.Join(",", cells),
                    signature);
            }

            result[oneBased - 1] = shapes;
        }

        return result;
    }

    /// <summary>
    /// Compares before/after and returns the losses found. An empty list means nothing that
    /// existed was damaged.
    /// </summary>
    /// <param name="allowNewShapes">
    /// Shapes that did not exist before are additions, not damage. Set false to treat any
    /// structural change as suspicious.
    /// </param>
    public static IReadOnlyList<FidelityLoss> Compare(
        IReadOnlyDictionary<int, Dictionary<string, ShapeFingerprint>> before,
        IReadOnlyDictionary<int, Dictionary<string, ShapeFingerprint>> after,
        bool allowNewShapes = true)
    {
        var losses = new List<FidelityLoss>();

        foreach (var (pageIndex, originalShapes) in before)
        {
            if (!after.TryGetValue(pageIndex, out var savedShapes))
            {
                losses.Add(new FidelityLoss
                {
                    Page = pageIndex,
                    Kind = FidelityLossKind.PageMissing,
                    Detail = $"页面 {pageIndex + 1} 在输出中消失"
                });
                continue;
            }

            foreach (var (id, original) in originalShapes)
            {
                if (!savedShapes.TryGetValue(id, out var saved))
                {
                    // A shape that vanished entirely. Deletion may be intentional, so this is
                    // reported rather than treated as damage; the caller decides.
                    losses.Add(new FidelityLoss
                    {
                        Page = pageIndex,
                        ShapeId = id,
                        Kind = FidelityLossKind.ShapeMissing,
                        Detail = $"形状 '{id}'（{original.NameU}）在输出中不存在"
                    });
                    continue;
                }

                // Geometry: a shape that had rows must not end up with fewer, and a shape that had
                // none must not silently acquire the synthesised rectangle — unless it is (or
                // becomes) a master instance, where materialising the outline is faithful.
                if (original.GeometryRows > 0 && saved.GeometryRows < original.GeometryRows)
                {
                    losses.Add(new FidelityLoss
                    {
                        Page = pageIndex,
                        ShapeId = id,
                        Kind = FidelityLossKind.GeometryReduced,
                        Detail = $"形状 '{id}' 的几何行数 {original.GeometryRows} → {saved.GeometryRows}"
                    });
                }

                if (original.GeometryRows == 0 && saved.GeometryRows > 0
                    && saved.Master is null && original.Master is null)
                {
                    losses.Add(new FidelityLoss
                    {
                        Page = pageIndex,
                        ShapeId = id,
                        Kind = FidelityLossKind.GeometryInjected,
                        Detail = $"形状 '{id}'（{original.NameU}）原本无几何，输出出现 {saved.GeometryRows} 行"
                    });
                }

                // Master link: losing it turns a master instance into a detached outline.
                if (original.Master is not null && saved.Master is null)
                {
                    losses.Add(new FidelityLoss
                    {
                        Page = pageIndex,
                        ShapeId = id,
                        Kind = FidelityLossKind.MasterLinkLost,
                        Detail = $"形状 '{id}' 的母版引用 '{original.Master}' 丢失"
                    });
                }

                // Cells: a shape must not lose cells it had.
                if (saved.CellCount < original.CellCount || saved.CellNames != original.CellNames)
                {
                    losses.Add(new FidelityLoss
                    {
                        Page = pageIndex,
                        ShapeId = id,
                        Kind = FidelityLossKind.CellsChanged,
                        Detail = $"形状 '{id}' 的单元格集合发生变化（{original.CellCount} → {saved.CellCount}）"
                    });
                }
            }

            if (allowNewShapes) continue;

            foreach (var id in savedShapes.Keys.Where(k => !originalShapes.ContainsKey(k)))
            {
                losses.Add(new FidelityLoss
                {
                    Page = pageIndex,
                    ShapeId = id,
                    Kind = FidelityLossKind.UnexpectedShape,
                    Detail = $"输出中出现未预期的形状 '{id}'"
                });
            }
        }

        return losses;
    }
}

public enum FidelityLossKind
{
    PageMissing,
    ShapeMissing,
    GeometryReduced,
    GeometryInjected,
    MasterLinkLost,
    CellsChanged,
    UnexpectedShape
}

public sealed class FidelityLoss
{
    public required int Page { get; init; }
    public string? ShapeId { get; init; }
    public required FidelityLossKind Kind { get; init; }
    public required string Detail { get; init; }

    /// <summary>True for losses that mean the output must not be handed back.</summary>
    public bool IsFatal => Kind is FidelityLossKind.GeometryInjected
        or FidelityLossKind.GeometryReduced
        or FidelityLossKind.MasterLinkLost;

    public override string ToString() => $"[{Kind}] {Detail}";
}
