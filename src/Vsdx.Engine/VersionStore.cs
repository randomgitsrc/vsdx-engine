using System.Text.Json;
using System.Text.Json.Serialization;
using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// One restorable state of a diagram: the model plus the quality report that accepted it.
///
/// Phase 0 found no snapshot mechanism at all — `SnapshotShape` is a formatting helper for
/// diff output, and `Metadata.History` is a text log. Neither can restore anything.
/// </summary>
public sealed class VersionSnapshot
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Deep copy of the model, so later edits cannot mutate this snapshot.</summary>
    public required Diagram Model { get; init; }

    /// <summary>Serialized model, kept for cheap comparison and on-disk persistence.</summary>
    public required string ModelJson { get; init; }

    public int HardViolations { get; init; }
    public int SoftViolations { get; init; }
    public int Score { get; init; }

    /// <summary>Issue codes present at capture time, for regression comparison.</summary>
    public required IReadOnlyList<string> IssueCodes { get; init; }

    /// <summary>Why this version exists, e.g. "baseline" or "fix: node.overlap".</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Stores diagram versions and decides whether a candidate is an improvement.
///
/// The rule that prevents the "each fix makes it worse" spiral is simple: a candidate is only
/// accepted if it removes at least one existing issue and introduces no new hard violation.
/// </summary>
public sealed class VersionStore
{
    private readonly List<VersionSnapshot> _snapshots = new();
    private readonly JsonSerializerOptions _json = DiagramJson.Options;

    public IReadOnlyList<VersionSnapshot> History => _snapshots;

    /// <summary>The last accepted version; the base for the next attempt.</summary>
    public VersionSnapshot? Current => _snapshots.Count > 0 ? _snapshots[^1] : null;

    /// <summary>Captures the model and its quality as a new version.</summary>
    public VersionSnapshot Capture(
        Diagram diagram, string label, LayoutReport? report = null, string? reason = null)
    {
        report ??= LayoutAnalyzer.Analyze(diagram);

        var snapshot = new VersionSnapshot
        {
            Id = $"v{_snapshots.Count + 1}",
            Label = label,
            Model = Clone(diagram),
            ModelJson = DiagramJson.Serialize(diagram),
            HardViolations = report.Errors.Count(),
            SoftViolations = report.Warnings.Count(),
            Score = report.Score,
            IssueCodes = report.Issues.Select(i => i.Code).OrderBy(c => c, StringComparer.Ordinal).ToList(),
            Reason = reason
        };

        _snapshots.Add(snapshot);
        return snapshot;
    }

    /// <summary>
    /// Decides whether <paramref name="candidate"/> is better than the current version.
    ///
    /// Acceptance requires progress *and* no regression: a candidate that fixes one issue while
    /// breaking something else is rejected, which is what stops the patch spiral.
    /// </summary>
    public AcceptanceDecision Evaluate(LayoutReport candidate, string? intendedIssueCode = null)
    {
        var current = Current;
        if (current is null)
        {
            return new AcceptanceDecision
            {
                Accepted = candidate.HardConstraintsPassed,
                Reason = candidate.HardConstraintsPassed
                    ? "首个版本，硬约束通过"
                    : "首个版本存在硬约束失败"
            };
        }

        var candidateCodes = candidate.Issues.Select(i => i.Code).ToHashSet(StringComparer.Ordinal);
        var currentCodes = current.IssueCodes.ToHashSet(StringComparer.Ordinal);

        // Any hard violation that is new relative to the current version is a regression.
        var newHard = candidate.Errors
            .Select(e => e.Code)
            .Where(c => !currentCodes.Contains(c))
            .Distinct()
            .ToList();

        if (newHard.Count > 0)
        {
            return new AcceptanceDecision
            {
                Accepted = false,
                Reason = $"引入新的硬约束失败：{string.Join(", ", newHard)}",
                NewHardViolations = newHard
            };
        }

        // Progress: fewer hard violations, or the same hard count with a lower issue total.
        int candidateTotal = candidate.Issues.Count;
        int currentTotal = current.HardViolations + current.SoftViolations;
        bool fewerHard = candidate.Errors.Count() < current.HardViolations;

        bool fixedIntended = intendedIssueCode is not null
                             && currentCodes.Contains(intendedIssueCode)
                             && !candidateCodes.Contains(intendedIssueCode);

        if (fewerHard || (candidate.Errors.Count() == current.HardViolations && candidateTotal < currentTotal) || fixedIntended)
        {
            return new AcceptanceDecision { Accepted = true, Reason = "改善了既有问题且未引入新问题" };
        }

        return new AcceptanceDecision
        {
            Accepted = false,
            Reason = "没有可测的改善（既未减少硬约束失败，也未减少问题总数）"
        };
    }

    /// <summary>
    /// Commits a candidate if it is an improvement. Returns the accepted snapshot, or null when
    /// the candidate was rejected and the current version was kept.
    /// </summary>
    public VersionSnapshot? TryAccept(
        Diagram candidateModel, string label, LayoutReport candidateReport, string? intendedIssueCode = null)
    {
        var decision = Evaluate(candidateReport, intendedIssueCode);
        if (!decision.Accepted) return null;

        return Capture(candidateModel, label, candidateReport, decision.Reason);
    }

    /// <summary>Restores the model of a previous version. The restore itself is recorded.</summary>
    public Diagram Rollback(string snapshotId)
    {
        var snapshot = _snapshots.FirstOrDefault(s => s.Id == snapshotId)
            ?? throw new InvalidOperationException($"未知版本 '{snapshotId}'");

        var restored = Clone(snapshot.Model);
        _snapshots.Add(new VersionSnapshot
        {
            Id = $"v{_snapshots.Count + 1}",
            Label = $"rollback to {snapshotId}",
            Model = Clone(restored),
            ModelJson = DiagramJson.Serialize(restored),
            HardViolations = snapshot.HardViolations,
            SoftViolations = snapshot.SoftViolations,
            Score = snapshot.Score,
            IssueCodes = snapshot.IssueCodes,
            Reason = $"回滚到 {snapshotId}"
        });

        return restored;
    }

    /// <summary>Rolls back to the most recent version that passed all hard constraints.</summary>
    public Diagram? RollbackToLastGood()
    {
        var good = _snapshots.LastOrDefault(s => s.HardViolations == 0);
        return good is null ? null : Rollback(good.Id);
    }

    /// <summary>Serializes a snapshot so it can be stored outside the process.</summary>
    public string ExportSnapshot(string snapshotId)
    {
        var snapshot = _snapshots.FirstOrDefault(s => s.Id == snapshotId)
            ?? throw new InvalidOperationException($"未知版本 '{snapshotId}'");
        return JsonSerializer.Serialize(snapshot, _json);
    }

    /// <summary>Deep copy via JSON round-trip; the model has no cycle that would break this.</summary>
    private Diagram Clone(Diagram source) =>
        JsonSerializer.Deserialize<Diagram>(DiagramJson.Serialize(source), _json)
        ?? throw new InvalidOperationException("模型克隆失败");
}

public sealed class AcceptanceDecision
{
    public bool Accepted { get; init; }
    public string Reason { get; init; } = "";
    public List<string> NewHardViolations { get; init; } = new();
}

/// <summary>
/// How far a repair must reach. Phase 0 recorded the three levels; this is the calculation that
/// chooses between them instead of letting the agent guess.
/// </summary>
public enum RepairScope
{
    /// <summary>One element; nothing else should move.</summary>
    Local,

    /// <summary>A whole container needs to be laid out again.</summary>
    RegionRelayout,

    /// <summary>The layout strategy itself is wrong; regenerate.</summary>
    FullRedraw
}

/// <summary>
/// Works out which objects a change will disturb, so the caller can pick a repair level instead
/// of nudging coordinates until it looks better.
/// </summary>
public sealed class ImpactAnalysis
{
    public required IReadOnlyList<string> DirectlyAffected { get; init; }
    public required IReadOnlyList<string> AffectedContainers { get; init; }
    public required IReadOnlyList<string> AffectedEdges { get; init; }
    public required IReadOnlyList<string> NeighbourNodes { get; init; }
    public required RepairScope Scope { get; init; }
    public required string Rationale { get; init; }

    /// <summary>
    /// Calculates what moving <paramref name="nodeIds"/> would disturb: their container, the
    /// other members of that container, and every edge attached to them.
    /// </summary>
    public static ImpactAnalysis ForNodes(
        Page page, IEnumerable<string> nodeIds, LayoutAnalyzerOptions? options = null)
    {
        options ??= new LayoutAnalyzerOptions();
        var ids = nodeIds.ToHashSet(StringComparer.Ordinal);
        var nodes = page.Nodes.Where(n => ids.Contains(n.Id)).ToList();

        var containers = nodes
            .Select(n => n.GroupId)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Siblings inside the same container: relaying out one member inevitably moves the rest.
        var siblings = page.Nodes
            .Where(n => n.GroupId is not null && containers.Contains(n.GroupId) && !ids.Contains(n.Id))
            .Select(n => n.Id)
            .ToList();

        var edges = page.Edges
            .Where(e => (e.From is not null && ids.Contains(e.From)) || (e.To is not null && ids.Contains(e.To)))
            .Select(e => e.Id)
            .ToList();

        bool touchesPageBounds = nodes.Any(n =>
            (page.Size is { Width: > 0 } size && n.Bounds.X + n.Bounds.W / 2 > size.Width - options.MinNodeGap));

        RepairScope scope;
        string rationale;

        if (nodes.Count <= 1 && containers.Count == 0 && !touchesPageBounds)
        {
            scope = RepairScope.Local;
            rationale = "单个元素且不属于任何容器：可局部调整，不牵动其它节点";
        }
        else if (containers.Count <= 1 && !touchesPageBounds)
        {
            scope = RepairScope.RegionRelayout;
            rationale = containers.Count == 0
                ? "触及页面边界，需重排所在区域"
                : $"位于容器 '{containers[0]}' 内，成员需整体重排";
        }
        else
        {
            scope = RepairScope.FullRedraw;
            rationale = containers.Count > 1
                ? $"跨 {containers.Count} 个容器，局部调整会互相影响"
                : "触及页面边界且影响多个区域";
        }

        return new ImpactAnalysis
        {
            DirectlyAffected = nodes.Select(n => n.Id).ToList(),
            AffectedContainers = containers,
            AffectedEdges = edges,
            NeighbourNodes = siblings,
            Scope = scope,
            Rationale = rationale
        };
    }
}
