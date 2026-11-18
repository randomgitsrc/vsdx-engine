using OfficeIMO;
using OfficeIMO.Visio;
using Vsdx.Adapters;
using Vsdx.Model;

namespace Vsdx.Renderer;

/// <summary>
/// Renders a saved VSDX package to SVG or PNG by re-opening the package and projecting its
/// real shape geometry. The renderer never builds an independent drawing: it always renders
/// the package on disk, so the preview reflects the actual deliverable.
/// </summary>
public sealed class OfficeImoVsdxRenderer : IDiagramRenderer
{
    public string Backend => "OfficeIMO.Visio";

    public RenderResult Render(RenderRequest request)
    {
        if (!File.Exists(request.InputPath))
            throw new FileNotFoundException($"VSDX not found: {request.InputPath}", request.InputPath);

        var result = new RenderResult { Backend = Backend };
        var doc = VisioDocument.Load(request.InputPath);

        var pages = SelectPages(doc, request, result.Compatibility);
        if (pages.Count == 0)
        {
            result.Compatibility.Error("render.no-pages", "No page matched the render request.");
            return result;
        }

        var requiresCjk = FontResolver.RequiresCjk(
            pages.SelectMany(p => p.Shapes.Select(s => s.Text)
                .Concat(p.Connectors.Select(c => c.Label))));

        var resolver = new FontResolver(request.FontSearchPaths);

        // Resolve from the document's own fonts before falling back to "whatever is available".
        // Without this the preview drew every diagram in the same face regardless of what the file
        // asked for: Georgia, Courier New and a nonexistent font produced identical PNGs, so the
        // preview disagreed with the deliverable it was meant to represent.
        //
        // An explicit --font-file still wins; this only supplies the value when the caller did not.
        string? fontFile = null;
        if (string.IsNullOrWhiteSpace(request.FontFile))
        {
            var requested = FirstRequestedFamily(pages);
            if (requested is not null)
            {
                fontFile = resolver.ResolveByFamily(requested, requiresCjk);
                if (fontFile is null)
                {
                    result.Compatibility.Warn("render.font-missing",
                        $"文档请求字体 '{requested}'，本机未安装，已回退到可用字体。预览与交付物可能不一致。",
                        requested);
                }
            }
        }

        fontFile ??= resolver.Resolve(requiresCjk, request.FontFile);
        result.FontUsed = fontFile;

        if (requiresCjk && fontFile is null)
            result.Compatibility.Warn("render.cjk-no-font",
                "Diagram contains CJK text but no CJK-capable font file was found; glyphs may render as boxes. Pass --font-file or install a CJK font.");

        var format = (request.Format ?? "png").Trim().ToLowerInvariant();
        var multiPage = pages.Count > 1;

        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var outputPath = ResolveOutputPath(request.OutputPath, multiPage, i, page, format);
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            try
            {
                if (format is "svg")
                {
                    var svgOptions = new VisioSvgSaveOptions { PageIndex = 0, BackgroundColor = ParseBackground(request) };
                    var svg = page.ToSvg(svgOptions);
                    File.WriteAllText(outputPath, svg);
                    result.Pages.Add(new RenderedPage
                    {
                        PageIndex = page.Id,
                        PageName = page.Name,
                        OutputPath = Path.GetFullPath(outputPath),
                        Format = "svg",
                        Bytes = new FileInfo(outputPath).Length
                    });
                }
                else
                {
                    var options = new VisioImageExportOptions
                    {
                        Format = OfficeIMO.Drawing.OfficeImageExportFormat.Png,
                        Scale = 1.0,
                        TargetDpi = request.Dpi,
                        RenderText = request.RenderText,
                        RenderConnectorLabels = true
                    };
                    if (fontFile is not null) options.FontFilePath = fontFile;
                    if (!string.IsNullOrWhiteSpace(request.FontFace)) options.FontFaceName = request.FontFace;

                    var backgroundColor = ParseBackground(request);
                    if (backgroundColor is not null) options.BackgroundColor = backgroundColor.Value;

                    if (File.Exists(outputPath)) File.Delete(outputPath);
                    page.SaveAsImage(outputPath, options);

                    var (width, height) = ReadPngSize(outputPath);
                    result.Pages.Add(new RenderedPage
                    {
                        PageIndex = page.Id,
                        PageName = page.Name,
                        OutputPath = Path.GetFullPath(outputPath),
                        Format = "png",
                        Bytes = new FileInfo(outputPath).Length,
                        Width = width,
                        Height = height
                    });
                }
            }
            catch (Exception ex)
            {
                result.Compatibility.Error("render.failed", $"Page '{page.Name}' failed to render: {ex.Message}", page.Name);
            }
        }

        if (result.Pages.Count > 0 && string.IsNullOrWhiteSpace(request.Background) && !request.Transparent)
            result.Compatibility.Info("render.background", "Rendered with the page background (default white).");

        return result;
    }

    private static OfficeIMO.OfficeColor? ParseBackground(RenderRequest request)
    {
        if (request.Transparent) return null;
        if (string.IsNullOrWhiteSpace(request.Background)) return null;
        if (request.Background.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return null;
        return ColorUtil.TryParse(request.Background, out var r, out var g, out var b)
            ? OfficeIMO.OfficeColor.FromRgb(r, g, b)
            : null;
    }

    private static List<OfficeIMO.Visio.VisioPage> SelectPages(
        VisioDocument doc, RenderRequest request, CompatibilityReport compat)
    {
        if (!string.IsNullOrWhiteSpace(request.PageName))
        {
            var byName = doc.Pages.FirstOrDefault(p =>
                string.Equals(p.Name, request.PageName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.NameU, request.PageName, StringComparison.OrdinalIgnoreCase));
            if (byName is null)
            {
                compat.Error("render.page-not-found", $"Page '{request.PageName}' not found. Available: {string.Join(", ", doc.Pages.Select(p => p.Name))}");
                return new List<OfficeIMO.Visio.VisioPage>();
            }
            return new List<OfficeIMO.Visio.VisioPage> { byName };
        }

        if (request.PageIndex is int index)
        {
            if (index < 0 || index >= doc.Pages.Count)
            {
                compat.Error("render.page-out-of-range", $"Page index {index} is out of range (0..{doc.Pages.Count - 1}).");
                return new List<OfficeIMO.Visio.VisioPage>();
            }
            return new List<OfficeIMO.Visio.VisioPage> { doc.Pages[index] };
        }

        return doc.Pages.ToList();
    }

    private static string ResolveOutputPath(string requested, bool multiPage, int index, OfficeIMO.Visio.VisioPage page, string format)
    {
        if (!multiPage) return requested;
        var dir = Path.GetDirectoryName(Path.GetFullPath(requested)) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(requested);
        var ext = string.IsNullOrEmpty(Path.GetExtension(requested)) ? "." + format : Path.GetExtension(requested);
        var safePage = SanitizeFileComponent(page.Name, page.Id);
        return Path.Combine(dir, $"{baseName}-{safePage}{ext}");
    }

    private static string SanitizeFileComponent(string name, int fallbackId)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallbackId.ToString();
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var cleaned = new string(chars).Trim('_');
        return string.IsNullOrEmpty(cleaned) ? fallbackId.ToString() : cleaned;
    }

    private static (int width, int height) ReadPngSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24];
            if (stream.Read(header) < 24) return (0, 0);
            // PNG signature (8 bytes) + IHDR length (4) + "IHDR" (4) + width (4) + height (4)
            if (header[0] != 0x89 || header[1] != 0x50) return (0, 0);
            int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return (width, height);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// The first font family the document actually asks for, or null when it names none.
    ///
    /// Fonts are per-shape, but the renderer resolves a single face for the whole page. Taking the
    /// first declared family makes the common case (one family for the diagram) render correctly,
    /// and is honest about the limitation rather than pretending to per-shape resolution.
    /// </summary>
    private static string? FirstRequestedFamily(IEnumerable<VisioPage> pages)
    {
        foreach (var page in pages)
        {
            foreach (var shape in page.Shapes)
            {
                var family = shape.TextStyle?.FontFamily;
                if (!string.IsNullOrWhiteSpace(family)) return family;
            }

            foreach (var connector in page.Connectors)
            {
                var family = connector.TextStyle?.FontFamily;
                if (!string.IsNullOrWhiteSpace(family)) return family;
            }
        }

        return null;
    }
}
