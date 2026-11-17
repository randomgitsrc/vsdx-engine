using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Runs an operation over many files, collecting per-item results so one failure does not
/// abort the batch. Batch output is always machine-readable, which is what a caller
/// orchestrating dozens of diagrams needs.
/// </summary>
public sealed class BatchRunner
{
    private readonly VsdxEngine _engine;

    public BatchRunner(VsdxEngine? engine = null) => _engine = engine ?? new VsdxEngine();

    /// <summary>Creates one VSDX per JSON model.</summary>
    public BatchResult CreateMany(IEnumerable<(string modelPath, string outputPath)> items, WriteOptions? options = null)
        => Run("create", items.Select(i => (i.modelPath, i.outputPath, (Func<string, string, BatchItem>)(
            (input, output) =>
            {
                var r = _engine.Create(input, output, options);
                return new BatchItem
                {
                    Input = input,
                    Output = r.OutputPath,
                    Ok = !r.Compatibility.Errors.Any(),
                    Detail = $"shapes={r.ShapesWritten} connectors={r.ConnectorsWritten}",
                    Issues = r.Compatibility.Issues.Count
                };
            }))));

    /// <summary>Renders one or more pages of each input to the given format.</summary>
    public BatchResult RenderMany(IEnumerable<(string inputPath, string outputPath)> items, RenderRequest template)
        => Run("render", items.Select(i => (i.inputPath, i.outputPath, (Func<string, string, BatchItem>)(
            (input, output) =>
            {
                var r = _engine.Render(new RenderRequest
                {
                    InputPath = input,
                    OutputPath = output,
                    Format = template.Format,
                    PageIndex = template.PageIndex,
                    PageName = template.PageName,
                    Dpi = template.Dpi,
                    Background = template.Background,
                    Transparent = template.Transparent,
                    FontFile = template.FontFile,
                    FontFace = template.FontFace,
                    RenderText = template.RenderText
                });
                return new BatchItem
                {
                    Input = input,
                    Output = string.Join(";", r.Pages.Select(p => p.OutputPath)),
                    Ok = r.Pages.Count > 0 && !r.Compatibility.Errors.Any(),
                    Detail = $"{r.Pages.Count} page(s)",
                    Issues = r.Compatibility.Issues.Count
                };
            }))));

    /// <summary>Validates many packages.</summary>
    public BatchResult ValidateMany(IEnumerable<string> paths, ValidationOptions? options = null)
        => Run("validate", paths.Select(p => (p, (string?)null, (Func<string, string, BatchItem>)(
            (input, _) =>
            {
                var r = _engine.Validate(input, options);
                return new BatchItem
                {
                    Input = input,
                    Output = null,
                    Ok = r.IsValid,
                    Detail = $"{r.Checks.Count(c => c.Passed)}/{r.Checks.Count} checks",
                    Issues = r.Errors.Count
                };
            }))));

    /// <summary>Exports many packages to JSON models.</summary>
    public BatchResult ExportMany(IEnumerable<(string inputPath, string outputPath)> items)
        => Run("export-model", items.Select(i => (i.inputPath, i.outputPath, (Func<string, string, BatchItem>)(
            (input, output) =>
            {
                var r = _engine.ExportModel(input, output);
                return new BatchItem
                {
                    Input = input,
                    Output = output,
                    Ok = true,
                    Detail = $"nodes={r.Diagram.Pages.Sum(p => p.Nodes.Count)} edges={r.Diagram.Pages.Sum(p => p.Edges.Count)}",
                    Issues = r.Compatibility.Issues.Count
                };
            }))));

    private static BatchResult Run(
        string operation,
        IEnumerable<(string input, string? output, Func<string, string, BatchItem> action)> items)
    {
        var result = new BatchResult { Operation = operation };

        foreach (var (input, output, action) in items)
        {
            result.Total++;
            try
            {
                var item = action(input, output ?? "");
                result.Items.Add(item);
                if (item.Ok) result.Succeeded++;
                else result.Failed++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Items.Add(new BatchItem
                {
                    Input = input,
                    Output = output,
                    Ok = false,
                    Error = $"{ex.GetType().Name}: {ex.Message}"
                });
            }
        }

        return result;
    }
}

public sealed class BatchResult
{
    public string Operation { get; init; } = "";
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public bool Ok => Failed == 0;
    public List<BatchItem> Items { get; } = new();
}

public sealed class BatchItem
{
    public string Input { get; set; } = "";
    public string? Output { get; set; }
    public bool Ok { get; set; }
    public string? Detail { get; set; }
    public string? Error { get; set; }
    public int Issues { get; set; }
}
