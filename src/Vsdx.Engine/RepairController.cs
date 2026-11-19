using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// Drives an improvement loop with explicit stopping rules.
///
/// The failure mode this exists to prevent (Phase 0 §4.1): notice a problem, nudge a node, a
/// connector breaks, nudge another, and after several rounds the diagram is worse than before.
/// Three mechanisms stop that — remembering which strategies already failed for a given issue,
/// escalating instead of retrying, and giving up after a bounded number of rounds.
/// </summary>
public sealed class RepairController
{
    private readonly VersionStore _versions = new();
    private readonly List<FixAttempt> _attempts = new();
    private readonly Dictionary<string, int> _failuresByCode = new(StringComparer.Ordinal);
    private readonly HashSet<string> _triedStrategies = new(StringComparer.Ordinal);

    private readonly int _maxAttemptsPerIssue;
    private readonly int _maxRounds;

    /// <param name="maxAttemptsPerIssue">
    /// Different strategies that may be tried for one issue code before escalating a level.
    /// </param>
    /// <param name="maxRounds">Total attempts before the loop stops and keeps the best version.</param>
    public RepairController(int maxAttemptsPerIssue = 2, int maxRounds = 8)
    {
        _maxAttemptsPerIssue = Math.Max(1, maxAttemptsPerIssue);
        _maxRounds = Math.Max(1, maxRounds);
    }

    public IReadOnlyList<FixAttempt> Attempts => _attempts;
    public VersionStore Versions => _versions;
    public bool Stopped { get; private set; }
    public string? StopReason { get; private set; }

    /// <summary>Captures the starting point.</summary>
    public VersionSnapshot Begin(Diagram diagram, string label = "baseline")
    {
        var report = LayoutAnalyzer.Analyze(diagram);
        return _versions.Capture(diagram, label, report, "起始版本");
    }

    /// <summary>
    /// Picks the next issue to attack and the scope its repair needs, or stops when the diagram
    /// passes hard constraints or the budget is exhausted.
    /// </summary>
    public RepairPlan? NextPlan(Diagram diagram)
    {
        if (Stopped) return null;
        if (_attempts.Count >= _maxRounds)
        {
            Stopped = true;
            StopReason = $"已达最大尝试轮数 {_maxRounds}";
            return null;
        }

        var report = LayoutAnalyzer.Analyze(diagram);

        if (report.HardConstraintsPassed && !report.Warnings.Any())
        {
            Stopped = true;
            StopReason = "硬约束与软约束均已通过";
            return null;
        }

        // Page-utilisation findings describe the canvas, not the arrangement. Relayout cannot
        // change them — only `--fit-page` can — so including them would make the loop chase a
        // fix it can never perform and prevent "clean" from ever being reached.
        static bool Actionable(LayoutIssue i) => i.Code != "page.underused" && i.Code != "page.margins";

        var issue = report.Errors.Where(Actionable)
                        .OrderByDescending(i => SeverityRank(i.Code))
                        .FirstOrDefault()
                    ?? report.Warnings.Where(Actionable)
                        .OrderByDescending(i => i.SoftWeight).FirstOrDefault();

        if (issue is null)
        {
            Stopped = true;
            StopReason = "没有可处理的问题";
            return null;
        }

        // If every repair level has already been tried for this code, another round cannot help.
        // Stopping here is what keeps the loop from grinding through its budget on known
        // failures — the exact behaviour that makes a diagram worse rather than better.
        var levels = new[] { RepairScope.Local, RepairScope.RegionRelayout, RepairScope.FullRedraw };
        if (levels.All(l => AlreadyTried(issue.Code, $"layout:{l}")))
        {
            Stopped = true;
            StopReason = $"'{issue.Code}' 的所有修复级别均已尝试且未改善，停止而非重复";
            return null;
        }

        // Escalate when this code has already burned its budget of different strategies.
        var scope = Escalate(issue);

        return new RepairPlan
        {
            IssueCode = issue.Code,
            Severity = issue.Severity,
            Targets = issue.Targets,
            SuggestedScope = scope,
            RootCause = issue.RootCause,
            RecommendedFix = issue.RecommendedFix,
            AttemptNumber = _attempts.Count + 1
        };
    }

    /// <summary>
    /// Records the outcome of applying a plan. Accepts the new version only when it measurably
    /// improves; otherwise the previous version stays current.
    /// </summary>
    public FixAttempt Record(RepairPlan plan, Diagram candidate, string strategy, string? expectedGain = null)
    {
        var candidateReport = LayoutAnalyzer.Analyze(candidate);
        var before = _versions.Current;
        var decision = _versions.Evaluate(candidateReport, plan.IssueCode);

        bool improved = decision.Accepted;
        VersionSnapshot? accepted = null;

        if (improved)
        {
            accepted = _versions.Capture(candidate, $"fix {plan.IssueCode} via {strategy}", candidateReport, decision.Reason);
            _failuresByCode.Remove(plan.IssueCode);
        }
        else
        {
            _failuresByCode[plan.IssueCode] = _failuresByCode.GetValueOrDefault(plan.IssueCode) + 1;
        }

        var attempt = new FixAttempt
        {
            Problem = plan.IssueCode,
            RootCause = plan.RootCause ?? plan.IssueCode,
            Strategy = strategy,
            Rationale = plan.RecommendedFix ?? "",
            ExpectedGain = expectedGain ?? $"消除 {plan.IssueCode}",
            ActualGain = improved
                // "问题数" counts errors and warnings; `info` notes are excluded because none of them is worth
                // repairing. `check-layout` reports the same page with a higher count for that reason, and the
                // wording says so rather than leaving the reader to notice the discrepancy.
                ? $"问题数（error+warning）{IssueCount(before)} → {IssueCount(accepted)}，分数 {before?.Score} → {accepted!.Score}"
                : null,
            IntroducedNewIssues = decision.NewHardViolations.Count > 0,
            NewIssueCodes = decision.NewHardViolations,
            Decision = improved ? FixDecision.Accepted : FixDecision.RolledBack,
            Note = decision.Reason
        };

        _attempts.Add(attempt);
        _triedStrategies.Add($"{plan.IssueCode}|{strategy}");
        return attempt;
    }

    /// <summary>
    /// True when this exact strategy has already failed for this issue, so it must not be
    /// retried — repeating a known-failed patch is the spiral this controller exists to stop.
    /// </summary>
    public bool AlreadyTried(string issueCode, string strategy) =>
        _triedStrategies.Contains($"{issueCode}|{strategy}");

    /// <summary>Escalates a repair when a code has consumed its budget of strategies.</summary>
    private RepairScope Escalate(LayoutIssue issue)
    {
        var baseScope = issue.SuggestedLevel switch
        {
            FixLevel.Local => RepairScope.Local,
            FixLevel.RegionRelayout => RepairScope.RegionRelayout,
            _ => RepairScope.FullRedraw
        };

        int failures = _failuresByCode.GetValueOrDefault(issue.Code);
        if (failures < _maxAttemptsPerIssue) return baseScope;

        // Budget exhausted at this level: climb one step rather than trying the same thing again.
        return baseScope switch
        {
            RepairScope.Local => RepairScope.RegionRelayout,
            RepairScope.RegionRelayout => RepairScope.FullRedraw,
            _ => RepairScope.FullRedraw
        };
    }

    private static int IssueCount(VersionSnapshot? snapshot) =>
        snapshot is null ? int.MaxValue : snapshot.HardViolations + snapshot.SoftViolations;

    private static int SeverityRank(string code) => code switch
    {
        "node.overlap" => 100,
        "group.overlap" => 95,
        "edge.through-node" => 90,
        "group.member-escapes" => 85,
        _ => 50
    };
}

public sealed class RepairPlan
{
    public required string IssueCode { get; init; }
    public required string Severity { get; init; }
    public required string[] Targets { get; init; }
    public required RepairScope SuggestedScope { get; init; }
    public string? RootCause { get; init; }
    public string? RecommendedFix { get; init; }
    public int AttemptNumber { get; init; }
}

/// <summary>One recorded repair attempt, so the loop's history is auditable.</summary>
public sealed class FixAttempt
{
    public required string Problem { get; init; }
    public required string RootCause { get; init; }
    public required string Strategy { get; init; }
    public required string Rationale { get; init; }
    public required string ExpectedGain { get; init; }
    public string? ActualGain { get; init; }
    public bool IntroducedNewIssues { get; init; }
    public List<string> NewIssueCodes { get; init; } = new();
    public FixDecision Decision { get; init; }
    public string? Note { get; init; }
}

public enum FixDecision
{
    Pending,
    Accepted,
    RolledBack
}
