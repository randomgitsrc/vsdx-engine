using OfficeIMO;
using OfficeIMO.Visio;
using Vsdx.Model;

namespace Vsdx.Adapters;

/// <summary>Single place that turns <see cref="ShapeStyle"/> into Visio ShapeSheet cells.</summary>
public static class ShapeStyler
{
    /// <summary>Applies a style wholesale (used when creating shapes).</summary>
    public static void Apply(VisioShape shape, ShapeStyle? style, Diagram? diagram = null)
    {
        var fill = ColorUtil.Normalize(style?.Fill);
        var stroke = ColorUtil.Normalize(style?.Stroke);

        if (fill is not null && ColorUtil.TryParse(fill, out var fr, out var fg, out var fb))
        {
            int pattern = 1;
            if (style?.FillPattern is not null && int.TryParse(style.FillPattern, out var fp)) pattern = fp;
            shape.SetFill(OfficeColor.FromRgb(fr, fg, fb), pattern);
        }

        if (stroke is not null && ColorUtil.TryParse(stroke, out var sr, out var sg, out var sb))
            shape.SetStroke(OfficeColor.FromRgb(sr, sg, sb), style?.StrokeWidth ?? 0.013);
        if (stroke is not null && style?.StrokePattern is int sp)
            shape.LinePattern = sp;

        var textColor = ColorUtil.Normalize(style?.TextColor);
        if (textColor is not null && ColorUtil.TryParse(textColor, out var tr, out var tg, out var tb))
            shape.SetTextColor(OfficeColor.FromRgb(tr, tg, tb));
        else if (fill is not null)
        {
            var readable = ColorUtil.ReadableTextColor(fill);
            if (ColorUtil.TryParse(readable, out var rr, out var rg, out var rb))
                shape.SetTextColor(OfficeColor.FromRgb(rr, rg, rb));
        }

        var font = style?.Font ?? diagram?.Style?.Font;
        if (!string.IsNullOrWhiteSpace(font)) shape.SetFont(font!);
        var fontSize = style?.FontSize ?? diagram?.Style?.FontSize;
        if (fontSize is double size) shape.SetFontSize(size);
        if (style?.Bold is bool bold) shape.SetBold(bold);
        if (style?.Italic is bool italic) shape.SetItalic(italic);

        if (style?.HAlign is not null || style?.VAlign is not null)
        {
            shape.TextStyle ??= new VisioTextStyle();
            shape.TextStyle.HorizontalAlignment = ParseAlign(style.HAlign);
            shape.TextStyle.VerticalAlignment = ParseVAlign(style.VAlign);
        }
    }

    /// <summary>
    /// Applies only differing properties, reporting whether anything actually changed.
    /// Used by incremental update so untouched shapes are never rewritten.
    /// </summary>
    public static bool ApplyChanges(VisioShape shape, ShapeStyle style, CompatibilityReport compat, string? target = null)
    {
        bool changed = false;

        var fill = ColorUtil.Normalize(style.Fill);
        if (fill is not null)
        {
            var current = OfficeImoDocumentReader.FormatColor(shape.FillColor);
            if (!string.Equals(current, fill, StringComparison.OrdinalIgnoreCase))
            {
                if (ColorUtil.TryParse(fill, out var r, out var g, out var b))
                {
                    int pattern = 1;
                    if (style.FillPattern is not null && int.TryParse(style.FillPattern, out var fp)) pattern = fp;
                    shape.SetFill(OfficeColor.FromRgb(r, g, b), pattern);
                    changed = true;
                }
            }
        }

        var stroke = ColorUtil.Normalize(style.Stroke);
        if (stroke is not null)
        {
            var current = OfficeImoDocumentReader.FormatColor(shape.LineColor);
            if (!string.Equals(current, stroke, StringComparison.OrdinalIgnoreCase))
            {
                if (ColorUtil.TryParse(stroke, out var r, out var g, out var b))
                {
                    shape.SetStroke(OfficeColor.FromRgb(r, g, b), style.StrokeWidth ?? shape.LineWeight);
                    changed = true;
                }
            }
        }

        if (style.StrokeWidth is double width && Math.Abs(shape.LineWeight - width) > 1e-9)
        {
            shape.SetStroke(shape.LineColor, width);
            changed = true;
        }

        var textColor = ColorUtil.Normalize(style.TextColor);
        if (textColor is not null)
        {
            var current = shape.TextStyle?.Color is OfficeColor c ? OfficeImoDocumentReader.FormatColor(c) : null;
            if (!string.Equals(current, textColor, StringComparison.OrdinalIgnoreCase))
            {
                if (ColorUtil.TryParse(textColor, out var r, out var g, out var b))
                {
                    shape.SetTextColor(OfficeColor.FromRgb(r, g, b));
                    changed = true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(style.Font))
        {
            var current = shape.TextStyle?.FontFamily;
            if (!string.Equals(current, style.Font, StringComparison.Ordinal))
            {
                shape.SetFont(style.Font!);
                changed = true;
            }
        }

        if (style.FontSize is double size2 && Math.Abs((shape.TextStyle?.Size ?? 0) - size2) > 1e-9)
        {
            shape.SetFontSize(size2);
            changed = true;
        }

        if (style.Bold is bool bold && (shape.TextStyle?.Bold ?? false) != bold)
        {
            shape.SetBold(bold);
            changed = true;
        }

        if (style.Italic is bool italic && (shape.TextStyle?.Italic ?? false) != italic)
        {
            shape.SetItalic(italic);
            changed = true;
        }

        return changed;
    }

    public static OfficeIMO.OfficeAlignment ParseAlign(string? value) => value?.ToLowerInvariant() switch
    {
        "left" or "start" => OfficeIMO.OfficeAlignment.Left,
        "right" or "end" => OfficeIMO.OfficeAlignment.Right,
        _ => OfficeIMO.OfficeAlignment.Center
    };

    public static VisioTextVerticalAlignment ParseVAlign(string? value) => value?.ToLowerInvariant() switch
    {
        "top" or "start" => VisioTextVerticalAlignment.Top,
        "bottom" or "end" => VisioTextVerticalAlignment.Bottom,
        _ => VisioTextVerticalAlignment.Middle
    };
}
