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
    public BatchResult CreateMany(
        IEnumerable<(string modelPath, string outputPath)> items,
        WriteOptions? options = null)
    {
        var work = items.Select(i => new BatchWork(i.modelPath, i.outputPath, (input, output) =>
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
        }));

        return Run("create", work);
    }

    /// <summary>Renders one or more pages of each input to the given format.</summary>
    public BatchResult RenderMany(
        IEnumerable<(string inputPath, string outputPath)> items,
        RenderRequest template)
    {
        var work = items.Select(i => new BatchWork(i.inputPath, i.outputPath, (input, output) =>
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
        }));

        return Run("render", work);
    }

    /// <summary>Validates many packages. Writes nothing, so no output path is required.</summary>
    public BatchResult ValidateMany(IEnumerable<string> paths, ValidationOptions? options = null)
    {
        var work = paths.Select(p => new BatchWork(p, null, (input, _) =>
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
        }));

        return Run("validate", work);
    }

    /// <summary>Exports many packages to JSON models.</summary>
    public BatchResult ExportMany(IEnumerable<(string inputPath, string outputPath)> items)
    {
        var work = items.Select(i => new BatchWork(i.inputPath, i.outputPath, (input, output) =>
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
        }));

        return Run("export-model", work);
    }

    private static BatchResult Run(string operation, IEnumerable<BatchWork> work)
    {
        var result = new BatchResult { Operation = operation };

        foreach (var item in work)
        {
            result.Total++;
            try
            {
                var outcome = item.Action(item.Input, item.Output ?? "");
                outcome.Output ??= item.Output;
                result.Items.Add(outcome);
                if (outcome.Ok) result.Succeeded++;
                else result.Failed++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Items.Add(new BatchItem
                {
                    Input = item.Input,
                    Output = item.Output,
                    Ok = false,
                    Error = $"{ex.GetType().Name}: {ex.Message}"
                });
            }
        }

        return result;
    }

    /// <summary>One unit of batch work: input path, optional output path, and the action.</summary>
    private sealed record BatchWork(string Input, string? Output, Func<string, string, BatchItem> Action);
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
