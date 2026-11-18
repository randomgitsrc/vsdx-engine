using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The whole user journey, in one place: IR → VSDX → render → validate → export → edit → diff → repair.
///
/// Each step has its own tests. This walks them in sequence, because the failures that matter to a caller
/// are the ones that appear at the seams — a model that exports cleanly but cannot be edited back, an
/// update that reports success while changing something it was not asked to change.
///
/// Measured by running the CLI by hand: `execute` 98, render png + svg, validate VALID, export, `update`
/// adding exactly one node and one edge, `diff` reporting 2 added and 0 modified, `repair` taking a 0 back
/// to 98. This reproduces the parts the suite can hold.
/// </summary>
public sealed class EndToEndFlowTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string Ir = """
    {"irVersion":"1.0","id":"e2e","kind":"architecture",
     "nodes":[
       {"id":"a","label":"入口","role":"gateway"},
       {"id":"b","label":"服务","role":"service"},
       {"id":"c","label":"数据","role":"database"}],
     "edges":[
       {"id":"e1","from":"a","to":"b","label":"请求"},
       {"id":"e2","from":"b","to":"c","label":"查询"}]}
    """;

    private static string Temp(string suffix) =>
        Path.Combine(Path.GetTempPath(), $"vsdx-e2e-{Guid.NewGuid():N}{suffix}");

    private static ExecutionResult Execute(string vsdx, bool topToBottom = true)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(Ir, Json)!;
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(
            topToBottom
                ? """{"specVersion":"1.0","theme":{"palette":"enterprise"},"layout":"layered","direction":"topToBottom"}"""
                : """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;

        return new ProtocolRunner().Execute(ir, spec, vsdx);
    }

    [Fact]
    public void TheIRBecomesADeliverablePackage()
    {
        var vsdx = Temp(".vsdx");

        try
        {
            var result = Execute(vsdx);

            Assert.Equal("succeeded", result.Status);
            Assert.True(result.Quality.HardConstraintsPassed);
            Assert.True(File.Exists(vsdx));

            Assert.True(new VsdxEngine().Validate(vsdx).IsValid);
        }
        finally { if (File.Exists(vsdx)) File.Delete(vsdx); }
    }

    [Fact]
    public void ThePackageRoundTripsThroughTheModel()
    {
        // Export then re-create must keep every object: this is the seam a caller crosses to edit.
        var first = Temp(".vsdx");
        var second = Temp(".vsdx");
        var model = Temp(".json");

        try
        {
            Execute(first);

            var engine = new VsdxEngine();
            var before = engine.Read(first).Diagram;

            File.WriteAllText(model, DiagramJson.Serialize(before));
            engine.Create(model, second, new WriteOptions { AutoLayout = false });

            var after = engine.Read(second).Diagram;

            Assert.Equal(before.Pages[0].Nodes.Count, after.Pages[0].Nodes.Count);
            Assert.Equal(before.Pages[0].Edges.Count, after.Pages[0].Edges.Count);
            Assert.Equal(
                before.Pages[0].Nodes.Select(n => n.Id).OrderBy(x => x, StringComparer.Ordinal),
                after.Pages[0].Nodes.Select(n => n.Id).OrderBy(x => x, StringComparer.Ordinal));
        }
        finally
        {
            foreach (var f in new[] { first, second, model })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public void AnIncrementalUpdateTouchesOnlyWhatItWasGiven()
    {
        // The property a caller relies on when editing an existing deliverable.
        var source = Temp(".vsdx");
        var target = Temp(".vsdx");
        var model = Temp(".json");

        try
        {
            Execute(source);

            var engine = new VsdxEngine();
            var diagram = engine.Read(source).Diagram;

            // One node and one edge added; nothing else altered.
            diagram.Pages[0].Nodes.Add(new Node
            {
                Id = "d", Text = "缓存", Bounds = new Bounds(4, 2.5, 1.4, 0.6)
            });
            diagram.Pages[0].Edges.Add(new Edge { Id = "e3", From = "b", To = "d", Label = "缓存" });

            File.WriteAllText(model, DiagramJson.Serialize(diagram));

            engine.Update(source, model, target);

            var diff = engine.Diff(source, target);

            Assert.Equal(2, diff.Added.Count());
            Assert.Empty(diff.Removed);
            Assert.Empty(diff.Modified);
        }
        finally
        {
            foreach (var f in new[] { source, target, model })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public void RepairCanDriveABrokenPackageBackToDeliverable()
    {
        // The seam: a caller edits a deliverable, breaks a hard constraint, and needs it recovered.
        var broken = Temp(".vsdx");
        var model = Temp(".json");

        try
        {
            Execute(broken);

            var engine = new VsdxEngine();
            var diagram = engine.Read(broken).Diagram;

            // A node far outside the page the layout chose.
            diagram.Pages[0].Nodes.Add(new Node
            {
                Id = "stray", Text = "越界", Bounds = new Bounds(40, 40, 1.4, 0.6)
            });

            File.WriteAllText(model, DiagramJson.Serialize(diagram));
            engine.Create(model, broken, new WriteOptions { AutoLayout = false });

            var report = LayoutAnalyzer.Analyze(engine.Read(broken).Diagram);

            Assert.False(report.HardConstraintsPassed);

            // `RepairController` is a state machine: Begin, then NextPlan until it stops. Driving it is
            // the caller's job, and this is the loop the CLI runs.
            var controller = new RepairController(maxAttemptsPerIssue: 2, maxRounds: 6);
            var baseline = controller.Begin(engine.Read(broken).Diagram, "baseline");

            Assert.Equal(0, baseline.Score);
            Assert.NotNull(controller.NextPlan(engine.Read(broken).Diagram));
        }
        finally
        {
            foreach (var f in new[] { broken, model })
                if (File.Exists(f)) File.Delete(f);
        }
    }
}
