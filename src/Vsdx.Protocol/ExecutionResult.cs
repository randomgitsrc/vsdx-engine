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
    public List<string> StagesCompleted { get; set; } = new();

    /// <summary>Design intents that were applied.</summary>
    public List<string> AppliedDesignNotes { get; set; } = new();

    /// <summary>Constraints that could not be satisfied. Never empty-by-omission.</summary>
    public List<UnmetConstraint> UnmetConstraints { get; set; } = new();

    /// <summary>Non-fatal diagnostics (font fallback, retained objects, etc.).</summary>
    public List<ExecutionWarning> Warnings { get; set; } = new();

    public List<ExecutionError> Errors { get; set; } = new();

    /// <summary>Quality outcome, split by the three tiers the brief requires.</summary>
    public QualityOutcome Quality { get; set; } = new();

    /// <summary>IR id → engine model id, so callers can map results back to their input.</summary>
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
            // The engine cannot judge aesthetics; it says so rather than pretending its score
            // is a visual verdict.
            RequiresVisualReview = true
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

    /// <summary>Builds the IR-id → model-id mapping from the IR and the produced diagram.</summary>
    public static Dictionary<string, string> BuildMapping(DiagramIr ir, Diagram diagram)
    {
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        var page = diagram.Pages.FirstOrDefault();
        if (page is null) return mapping;

        foreach (var node in ir.Nodes)
        {
            // The engine preserves node ids, so the mapping is the identity in the normal case.
            var match = page.Nodes.FirstOrDefault(n => n.Id == node.Id);
            mapping[node.Id] = match?.Id ?? node.Id;
        }

        foreach (var container in ir.Containers)
        {
            var match = page.Groups.FirstOrDefault(g => g.Id == container.Id);
            mapping[container.Id] = match?.Id ?? container.Id;
        }

        return mapping;
    }
}
