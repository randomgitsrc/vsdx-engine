namespace Vsdx.Model;

/// <summary>
/// Backend-neutral read access to a VSDX package. Implementations must not require
/// Microsoft Visio at runtime.
/// </summary>
public interface IDocumentReader
{
    /// <summary>Reads a VSDX package into the JSON model. Never throws for content it cannot map.</summary>
    ReadResult Read(string vsdxPath);
}

/// <summary>Backend-neutral write access. Writes the JSON model to a VSDX package.</summary>
public interface IDocumentWriter
{
    WriteResult Write(Diagram diagram, string vsdxPath, WriteOptions? options = null);
}

/// <summary>Reads a package and preserves unmapped content for a subsequent incremental save.</summary>
public interface IIncrementalDocumentEditor
{
    /// <summary>
    /// Applies <paramref name="model"/> onto an existing package. Objects present in the
    /// package but absent from the model are retained unless explicitly deleted.
    /// </summary>
    UpdateResult Update(string sourceVsdxPath, Diagram model, string outputVsdxPath, UpdateOptions? options = null);
}

public interface IDiagramRenderer
{
    RenderResult Render(RenderRequest request);
}

public interface IDocumentValidator
{
    ValidationResult Validate(string vsdxPath, ValidationOptions? options = null);
}

public sealed class ReadResult
{
    public required Diagram Diagram { get; init; }
    public CompatibilityReport Compatibility { get; init; } = new();
    public string SourcePath { get; init; } = "";
    public string Backend { get; init; } = "";
    public string BackendVersion { get; init; } = "";
    public long FileSize { get; init; }
}

public sealed class WriteResult
{
    public required string OutputPath { get; init; }
    public CompatibilityReport Compatibility { get; init; } = new();
    public int PagesWritten { get; init; }
    public int ShapesWritten { get; init; }
    public int ConnectorsWritten { get; init; }
    public long FileSize { get; init; }
    public string Backend { get; init; } = "";
}

public sealed class UpdateResult
{
    public required string OutputPath { get; init; }
    public CompatibilityReport Compatibility { get; init; } = new();
    public List<ModelChange> Changes { get; init; } = new();
    public int NodesAdded { get; init; }
    public int NodesModified { get; init; }
    public int NodesDeleted { get; init; }
    public int EdgesAdded { get; init; }
    public int EdgesModified { get; init; }
    public int EdgesDeleted { get; init; }
    public int NodesPreserved { get; init; }
    public long FileSize { get; init; }
    public string Backend { get; init; } = "";
}

public sealed class ModelChange
{
    public string Kind { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Detail { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
}

public sealed class RenderRequest
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    /// <summary>png | svg</summary>
    public string Format { get; init; } = "png";
    /// <summary>Zero-based page index, or null for all pages.</summary>
    public int? PageIndex { get; init; }
    /// <summary>Specific page name.</summary>
    public string? PageName { get; init; }
    /// <summary>Output pixels per inch for raster output.</summary>
    public double Dpi { get; init; } = 144;
    /// <summary>Optional background; null keeps the page background.</summary>
    public string? Background { get; init; }
    public bool Transparent { get; init; }
    /// <summary>Explicit font file for glyph fallback (TrueType/OpenType).</summary>
    public string? FontFile { get; init; }
    public string? FontFace { get; init; }
    /// <summary>Additional directories to search for fonts.</summary>
    public IReadOnlyList<string>? FontSearchPaths { get; init; }
    public bool RenderText { get; init; } = true;
}

public sealed class RenderResult
{
    public List<RenderedPage> Pages { get; } = new();
    public CompatibilityReport Compatibility { get; } = new();
    public string Backend { get; init; } = "";
    public string? FontUsed { get; set; }
}

public sealed class RenderedPage
{
    public int PageIndex { get; init; }
    public string PageName { get; init; } = "";
    public required string OutputPath { get; init; }
    public string Format { get; init; } = "png";
    public long Bytes { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class ValidationOptions
{
    public bool Deep { get; init; } = true;
    public bool RenderSmoke { get; init; }
}

public sealed class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationIssue> Errors { get; } = new();
    public List<ValidationIssue> Warnings { get; } = new();
    public List<ValidationCheck> Checks { get; } = new();
    public void Error(string code, string message, string? target = null) =>
        Errors.Add(new ValidationIssue { Code = code, Message = message, Target = target });
    public void Warn(string code, string message, string? target = null) =>
        Warnings.Add(new ValidationIssue { Code = code, Message = message, Target = target });
    public void Check(string name, bool passed, string? detail = null) =>
        Checks.Add(new ValidationCheck { Name = name, Passed = passed, Detail = detail });
}

public sealed class ValidationIssue
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Target { get; set; }
}

public sealed class ValidationCheck
{
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public string? Detail { get; set; }
}

public sealed class WriteOptions
{
    public bool ValidateAfterSave { get; init; } = true;
    /// <summary>Derive container bounds from member geometry before writing.</summary>
    public bool AutoLayout { get; init; } = true;

    /// <summary>
    /// Resize nodes, containers and the page from their text before laying out.
    ///
    /// Off by default so an author who already sized every shape keeps exact control; turn it
    /// on to let the engine derive geometry from the labels instead.
    /// </summary>
    public bool FitToText { get; init; }

    /// <summary>Grow the page to contain the drawing plus a margin.</summary>
    public bool FitPage { get; init; }

    /// <summary>Shift the drawing so it sits inside the page with equal margins.</summary>
    public bool Recenter { get; init; }

    /// <summary>
    /// Layout strategy name: layered / grid / flow / tree / manual / auto.
    /// "auto" picks a strategy from the diagram's declared type.
    /// </summary>
    public string? Layout { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public bool RequestRecalc { get; init; } = true;
}

public sealed class UpdateOptions
{
    /// <summary>When true, objects in the package but missing from the model are deleted.</summary>
    public bool DeleteMissing { get; init; }
    /// <summary>Derive container bounds and route orthogonal edges before applying.</summary>
    public bool AutoLayout { get; init; } = true;
    public bool ValidateAfterSave { get; init; } = true;
    public bool RecordHistory { get; init; } = true;
}
