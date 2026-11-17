using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// RepairTests: versioning, impact analysis and the stopping rules that prevent the "each fix
/// makes it worse" spiral described in the brief's §4.1.
/// </summary>
public sealed class RepairTests
{
    private static Diagram Cluttered()
    {
        // Two overlapping nodes in one container: a real hard violation to repair.
        return new Diagram
        {
            PageSize = new PageSize { Width = 12, Height = 8 },
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 12, Height = 8 },
                    Groups = { new Group { Id = "g", Name = "Band", Members = { "a", "b" } } },
                    Nodes =
                    {
                        new Node { Id = "a", Text = "A", Bounds = new Bounds(4, 5, 2, 1), GroupId = "g" },
                        new Node { Id = "b", Text = "B", Bounds = new Bounds(4.6, 5, 2, 1), GroupId = "g" }
                    }
                }
            }
        };
    }

    private static Diagram Clean()
    {
        // Page deliberately sized to the content: the point of this fixture is "no layout
        // defects", not "generous canvas".
        return new Diagram
        {
            // Sized so content fills ~45% of the page: this fixture tests "no defects", not
            // "generous canvas", so page utilisation must not become the finding.
            PageSize = new PageSize { Width = 6.0, Height = 2.6 },
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 6.0, Height = 2.6 },
                    Nodes =
                    {
                        new Node { Id = "a", Text = "A", Bounds = new Bounds(1.6, 1.3, 1.8, 1.0) },
                        new Node { Id = "b", Text = "B", Bounds = new Bounds(4.2, 1.3, 1.8, 1.0) }
                    }
                }
            }
        };
    }

    [Fact]
    public void VersionStore_CapturesAndRestores()
    {
        var store = new VersionStore();
        var diagram = Cluttered();

        var baseline = store.Capture(diagram, "baseline");

        // Mutate the live model; the snapshot must be unaffected.
        diagram.Pages[0].Nodes[0].Text = "changed";
        diagram.Pages[0].Nodes[0].Bounds.X = 99;

        Assert.Equal("A", baseline.Model.Pages[0].Nodes[0].Text);
        Assert.Equal(4, baseline.Model.Pages[0].Nodes[0].Bounds.X);

        var restored = store.Rollback(baseline.Id);
        Assert.Equal("A", restored.Pages[0].Nodes[0].Text);
        Assert.Equal(4, restored.Pages[0].Nodes[0].Bounds.X);
    }

    [Fact]
    public void VersionStore_ReproducesModelJson()
    {
        var store = new VersionStore();
        var diagram = Clean();
        var snapshot = store.Capture(diagram, "baseline");

        var reloaded = DiagramJson.Deserialize(snapshot.ModelJson);
        Assert.Equal(diagram.Pages[0].Nodes.Count, reloaded.Pages[0].Nodes.Count);
        Assert.Equal(diagram.Pages[0].Nodes[0].Bounds.X, reloaded.Pages[0].Nodes[0].Bounds.X, 3);
    }

    [Fact]
    public void VersionStore_RejectsCandidateThatAddsHardViolation()
    {
        var store = new VersionStore();
        store.Capture(Clean(), "clean");

        // Worse model: nodes now overlap.
        var worse = Cluttered();
        var report = LayoutAnalyzer.Analyze(worse);

        var decision = store.Evaluate(report);

        Assert.False(decision.Accepted);
        Assert.NotEmpty(decision.NewHardViolations);
    }

    [Fact]
    public void VersionStore_AcceptsCandidateThatFixesHardViolation()
    {
        var store = new VersionStore();
        store.Capture(Cluttered(), "cluttered");

        var report = LayoutAnalyzer.Analyze(Clean());
        var decision = store.Evaluate(report);

        Assert.True(decision.Accepted);
    }

    [Fact]
    public void VersionStore_RejectsNoOpChange()
    {
        var store = new VersionStore();
        var diagram = Cluttered();
        store.Capture(diagram, "baseline");

        // Identical state, no improvement.
        var report = LayoutAnalyzer.Analyze(Cluttered());
        var decision = store.Evaluate(report);

        Assert.False(decision.Accepted);
        Assert.Contains("没有可测的改善", decision.Reason);
    }

    [Fact]
    public void VersionStore_RollbackToLastGood()
    {
        var store = new VersionStore();
        store.Capture(Clean(), "good");
        store.Capture(Cluttered(), "bad");

        var restored = store.RollbackToLastGood();

        Assert.NotNull(restored);
        // The restored model must match the Clean() fixture's first node position.
        Assert.Equal(1.6, restored!.Pages[0].Nodes[0].Bounds.X, 3);
    }

    [Fact]
    public void VersionStore_RollbackRecordsItself()
    {
        var store = new VersionStore();
        var baseline = store.Capture(Clean(), "baseline");
        int before = store.History.Count;

        store.Rollback(baseline.Id);

        Assert.Equal(before + 1, store.History.Count);
        Assert.Contains("回滚", store.Current!.Reason);
    }

    [Fact]
    public void Impact_SingleNodeOutsideContainerIsLocal()
    {
        var page = Clean().Pages[0];
        var impact = ImpactAnalysis.ForNodes(page, new[] { "a" });

        Assert.Equal(RepairScope.Local, impact.Scope);
        Assert.Empty(impact.AffectedContainers);
    }

    [Fact]
    public void Impact_NodeInContainerForcesRegionRelayout()
    {
        var page = Cluttered().Pages[0];
        var impact = ImpactAnalysis.ForNodes(page, new[] { "a" });

        Assert.Equal(RepairScope.RegionRelayout, impact.Scope);
        Assert.Contains("g", impact.AffectedContainers);
        Assert.Contains("b", impact.NeighbourNodes);
    }

    [Fact]
    public void Impact_MultipleContainersForcesFullRedraw()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 5.5, Height = 6.0 },
            Groups =
            {
                new Group { Id = "g1", Members = { "a" } },
                new Group { Id = "g2", Members = { "b" } }
            },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(2.75, 4.3, 2, 1), GroupId = "g1" },
                new Node { Id = "b", Bounds = new Bounds(2.75, 1.9, 2, 1), GroupId = "g2" }
            }
        };

        var impact = ImpactAnalysis.ForNodes(page, new[] { "a", "b" });

        Assert.Equal(RepairScope.FullRedraw, impact.Scope);
        Assert.Equal(2, impact.AffectedContainers.Count);
    }

    [Fact]
    public void Impact_ReportsAttachedEdges()
    {
        var page = Clean().Pages[0];
        page.Edges.Add(new Edge { Id = "e1", From = "a", To = "b" });

        var impact = ImpactAnalysis.ForNodes(page, new[] { "a" });

        Assert.Contains("e1", impact.AffectedEdges);
    }

    [Fact]
    public void Impact_AlwaysExplainsItself()
    {
        var impact = ImpactAnalysis.ForNodes(Clean().Pages[0], new[] { "a" });
        Assert.False(string.IsNullOrWhiteSpace(impact.Rationale));
    }

    [Fact]
    public void Controller_StopsWhenClean()
    {
        var controller = new RepairController();
        var diagram = Clean();
        controller.Begin(diagram);

        var plan = controller.NextPlan(diagram);

        Assert.Null(plan);
        Assert.True(controller.Stopped);
    }

    [Fact]
    public void Controller_PlansHardViolationsFirst()
    {
        var controller = new RepairController();
        var diagram = Cluttered();
        controller.Begin(diagram);

        var plan = controller.NextPlan(diagram);

        Assert.NotNull(plan);
        Assert.Equal("node.overlap", plan!.IssueCode);
    }

    [Fact]
    public void Controller_AcceptsRealImprovement()
    {
        var controller = new RepairController();
        controller.Begin(Cluttered());

        var plan = controller.NextPlan(Cluttered())!;
        var attempt = controller.Record(plan, Clean(), "separate nodes");

        Assert.Equal(FixDecision.Accepted, attempt.Decision);
        Assert.NotNull(attempt.ActualGain);
    }

    [Fact]
    public void Controller_RejectsRegressionAndKeepsBaseline()
    {
        var controller = new RepairController();
        var baseline = controller.Begin(Cluttered());

        var plan = controller.NextPlan(Cluttered())!;

        // "Fix" that moves the overlap somewhere else: one hard violation traded for another.
        var worse = new Diagram
        {
            PageSize = new PageSize { Width = 12, Height = 8 },
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 12, Height = 8 },
                    Groups = { new Group { Id = "g", Members = { "a", "b" } } },
                    Nodes =
                    {
                        new Node { Id = "a", Bounds = new Bounds(3, 2, 2, 1), GroupId = "g" },
                        new Node { Id = "b", Bounds = new Bounds(3.5, 2, 2, 1), GroupId = "g" }
                    }
                }
            }
        };

        var attempt = controller.Record(plan, worse, "nudge");

        Assert.Equal(FixDecision.RolledBack, attempt.Decision);
        Assert.Equal(baseline.Id, controller.Versions.Current!.Id);
    }

    [Fact]
    public void Controller_RemembersFailedStrategies()
    {
        var controller = new RepairController();
        controller.Begin(Cluttered());

        var plan = controller.NextPlan(Cluttered())!;
        controller.Record(plan, Cluttered(), "no-op nudge");

        Assert.True(controller.AlreadyTried(plan.IssueCode, "no-op nudge"));
        Assert.False(controller.AlreadyTried(plan.IssueCode, "different approach"));
    }

    [Fact]
    public void Controller_EscalatesAfterRepeatedFailures()
    {
        // After the per-issue budget is spent, the plan must climb a repair level instead of
        // retrying the same scope.
        var controller = new RepairController(maxAttemptsPerIssue: 1, maxRounds: 6);
        controller.Begin(Cluttered());

        // node.overlap is regional by nature: one of two intersecting shapes cannot be moved in
        // isolation without risking a new collision.
        var first = controller.NextPlan(Cluttered())!;
        Assert.Equal(RepairScope.RegionRelayout, first.SuggestedScope);

        controller.Record(first, Cluttered(), "attempt 1");
        var second = controller.NextPlan(Cluttered());

        // The budget for this issue is spent, so the plan must escalate rather than repeat.
        Assert.NotNull(second);
        Assert.Equal(RepairScope.FullRedraw, second!.SuggestedScope);
    }

    [Fact]
    public void Controller_StopsAtMaxRounds()
    {
        var controller = new RepairController(maxRounds: 2);
        controller.Begin(Cluttered());

        for (int i = 0; i < 2; i++)
        {
            var plan = controller.NextPlan(Cluttered());
            if (plan is null) break;
            controller.Record(plan, Cluttered(), $"attempt {i}");
        }

        Assert.Null(controller.NextPlan(Cluttered()));
        Assert.True(controller.Stopped);
        Assert.Contains("最大尝试轮数", controller.StopReason);
    }

    [Fact]
    public void Controller_RecordsEveryAttemptForAudit()
    {
        var controller = new RepairController();
        controller.Begin(Cluttered());

        var plan = controller.NextPlan(Cluttered())!;
        controller.Record(plan, Clean(), "separate");

        var attempt = controller.Attempts[0];
        Assert.False(string.IsNullOrWhiteSpace(attempt.Problem));
        Assert.False(string.IsNullOrWhiteSpace(attempt.RootCause));
        Assert.False(string.IsNullOrWhiteSpace(attempt.Strategy));
        Assert.False(string.IsNullOrWhiteSpace(attempt.ExpectedGain));
    }
}
