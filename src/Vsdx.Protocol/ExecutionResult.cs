using System.Text.Json;
using System.Text.Json.Serialization;
using Vsdx.Engine;
using Vsdx.Model;

namespace Vsdx.Protocol;

/// <summary>
/// Execution Result — *what the engine actually did*, including everything it could not do.
///
/// Phase 7 §7.1 requires the result to carry status, diagnostics, unmet constraints, degradation
/// and the object mapping back to the source IR. The governing rule is that a caller can always
/// tell the difference between "done", "done with compromises" and "failed".
/// </summary>
public sealed class ExecutionResult
{
    public string ResultVersion { get; set; } = ProtocolVersion.ExecutionResult;

    /// <summary>Version of the IR this result was produced from.</summary>
    public string? IrVersion { get; set; }

    /// <summary>Version of the design spec, when one was applied.</summary>
    public string? DesignSpecVersion { get; set; }

    /// <summary>
    /// succeeded | partial | failed | unsupported | degraded.
    /// "partial" means output exists but some hard constraint was not met.
    /// "degraded" means a capability was unavailable and a fallback was used.
    /// </summary>
    public string Status { get; set; } = ExecutionStatus.Failed;

    public string? Message { get; set; }

    /// <summary>Which pipeline stages ran, in order.</summary>
    /// <summary>
    /// Stages that finished, in order. Empty when validation fails; partial when a later stage throws.
    ///
    /// Unlike <see cref="QualityOutcome.RequiresVisualReview"/> (which was once constant) and
    /// <see cref="ObjectMapping"/> (which is the identity), this field carries real information: the
    /// same IR run three ways reports three different lists. Verified in `StageReportingTests`.
    /// </summary>
    public List<string> StagesCompleted { get; set; } = new();

    /// <summary>Design intents that were applied.</summary>
    /// <summary>
    /// Steps that actually ran, phrased so each can be checked against the written file.
    ///
    /// Verified one by one against the produced package (`AppliedNotesTruthfulnessTests`), not against
    /// the code that writes them: the palette note names colours present on the nodes, the background
    /// note matches `PageBackground`, the paper note names the size the page is, `sizing` means long
    /// labels produced wider boxes, and `container.bands` means containers carry fill and a bold
    /// heading. `decoration.clearance` and `label.spread` appear only when something moved.
    ///
    /// This matters because the opposite failure — claiming work that did not happen — reads like
    /// diligence while being just as untrustworthy as silence.
    /// </summary>
    public List<string> AppliedDesignNotes { get; set; } = new();

    /// <summary>Constraints that could not be satisfied. Never empty-by-omission.</summary>
    public List<UnmetConstraint> UnmetConstraints { get; set; } = new();

    /// <summary>Non-fatal diagnostics (font fallback, retained objects, etc.).</summary>
    public List<ExecutionWarning> Warnings { get; set; } = new();

    public List<ExecutionError> Errors { get; set; } = new();

    /// <summary>Quality outcome, split by the three tiers the brief requires.</summary>
    public QualityOutcome Quality { get; set; } = new();

    /// <summary>
    /// IR id → engine model id, so callers can map results back to their input.
    ///
    /// **These currently coincide**: an IR object's `id` becomes the model object's `Id` verbatim, so
    /// every entry is the identity (`web → web`, measured: 22 entries on the cross-border diagram, 0
    /// non-identity). The dictionary stays for the case where an author-supplied id is ever remapped,
    /// but today it carries no information a caller did not already have.
    ///
    /// It is **not** a lookup from model id to the integer `Shape ID` written inside the package —
    /// that value is not exposed anywhere in the result.
    /// </summary>
    public Dictionary<string, string> ObjectMapping { get; set; } = new();

    /// <summary>Rendered outputs, when rendering was requested.</summary>
    public List<RenderOutput> Renders { get; set; } = new();

    /// <summary>Paths written by this execution.</summary>
    public List<string> OutputFiles { get; set; } = new();

    public long DurationMs { get; set; }

    public DateTimeOffset CompletedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class ExecutionStatus
{
    public const string Succeeded = "succeeded";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Unsupported = "unsupported";
    public const string Degraded = "degraded";
}

public sealed class QualityOutcome
{
    /// <summary>Hard constraints — failure means the deliverable is not acceptable.</summary>
    public bool HardConstraintsPassed { get; set; }

    public int HardViolations { get; set; }
    public int SoftViolations { get; set; }

    /// <summary>Soft score 0..100; only meaningful when hard constraints pass.</summary>
    public int Score { get; set; }

    /// <summary>Issue codes, so a caller can group/diff without parsing messages.</summary>
    public List<string> IssueCodes { get; set; } = new();

    /// <summary>Page utilisation per page, as a fraction.</summary>
    public Dictionary<string, double> PageUtilisation { get; set; } = new();

    /// <summary>
    /// True when this result required human/visual judgement that the engine cannot make.
    /// The engine states this rather than implying its score is a visual verdict.
    /// </summary>
    public bool RequiresVisualReview { get; set; }
}

public sealed class ExecutionWarning
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Target { get; set; }
}

public sealed class ExecutionError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Target { get; set; }
}

public sealed class RenderOutput
{
    public string Format { get; set; } = "png";
    public string Path { get; set; } = "";
    public int PageIndex { get; set; }
    public string? PageName { get; set; }
    public long Bytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>Builds an <see cref="ExecutionResult"/> from engine primitives.</summary>
public static class ExecutionResultBuilder
{
    /// <summary>
    /// Derives the status. The rule: hard-constraint failure outranks everything; otherwise an
    /// unmet hard design constraint makes it partial, and a fallback makes it degraded.
    /// </summary>
    public static string DeriveStatus(
        bool anyErrors, bool hardPassed, bool anyUnmetHardConstraint, bool anyDegradation)
    {
        if (anyErrors) return ExecutionStatus.Failed;
        if (!hardPassed || anyUnmetHardConstraint) return ExecutionStatus.Partial;
        if (anyDegradation) return ExecutionStatus.Degraded;
        return ExecutionStatus.Succeeded;
    }

    /// <summary>Builds a quality block from a layout report.</summary>
    public static QualityOutcome FromReport(LayoutReport report)
    {
        return new QualityOutcome
        {
            HardConstraintsPassed = report.HardConstraintsPassed,
            HardViolations = report.Errors.Count(),
            SoftViolations = report.Warnings.Count(),
            Score = report.Score,
            IssueCodes = report.Issues.Select(i => i.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList(),
            PageUtilisation = report.PageUtilisation.ToDictionary(kv => kv.Key, kv => kv.Value),

            // Set only when there is something a human might see that the analyser structurally
            // cannot: every violation is a place where geometry is wrong, and the render adds things
            // geometry does not describe — connection labels are drawn at call time and belong to no
            // bounding box. A diagram with no violations has no such gap.
            //
            // It used to be `true` unconditionally, including for a one-node diagram scoring 0. A flag
            // that is always on tells a caller nothing, which is worse than not having one.
            RequiresVisualReview = report.Errors.Any() || report.Warnings.Any()
        };
    }

    /// <summary>Converts engine compatibility warnings into protocol warnings.</summary>
    public static List<ExecutionWarning> FromCompatibility(CompatibilityReport report)
    {
        return report.Issues.Select(i => new ExecutionWarning
        {
            Code = i.Code,
            Message = i.Message,
            Target = i.Target
        }).ToList();
    }

    /// <summary>
    /// Maps every protocol object id to the shape that carries it.
    ///
    /// Covers all object kinds and all pages. The earlier version walked only nodes and
    /// containers on the first page, so edges and text blocks were unmapped even in a one-page
    /// diagram, and a paged IR — whose shorthand lists are empty — produced an empty map despite
    /// the document being written. Anything driving incremental edits needs the full set: without
    /// an entry for an edge or a label there is no route from its IR id to the shape holding it.
    ///
    /// Ids are expected to be unique across the whole IR, so a dictionary keyed by id is correct;
    /// a duplicate would silently collapse two objects onto one entry, which the validator rejects
    /// before this runs.
    /// </summary>
    public static Dictionary<string, string> BuildMapping(DiagramIr ir, Diagram diagram)
    {
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);

        // Paged input carries its objects per page; the flat lists are the single-page shorthand.
        // Take both, so neither form is silently dropped.
        var sources = new List<IrPage>();
        if (ir.Pages.Count > 0) sources.AddRange(ir.Pages);

        if (ir.Nodes.Count > 0 || ir.Containers.Count > 0 || ir.Edges.Count > 0
            || ir.TextBlocks.Count > 0 || ir.Paths.Count > 0)
        {
            sources.Add(new IrPage
            {
                Id = ir.Id,
                Nodes = ir.Nodes,
                Edges = ir.Edges,
                Containers = ir.Containers,
                TextBlocks = ir.TextBlocks,
                Paths = ir.Paths
            });
        }

        foreach (var source in sources)
        {
            foreach (var node in source.Nodes) mapping[node.Id] = node.Id;
            foreach (var container in source.Containers) mapping[container.Id] = container.Id;
            foreach (var edge in source.Edges) mapping[edge.Id] = edge.Id;
            foreach (var text in source.TextBlocks) mapping[text.Id] = text.Id;
            foreach (var path in source.Paths) mapping[path.Id] = path.Id;
        }

        return mapping;
    }
}
