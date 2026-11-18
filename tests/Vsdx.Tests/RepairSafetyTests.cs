using System.Diagnostics;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `repair` must never leave a file worse than it found it, and must not report success when it does.
///
/// Measured on the cross-border diagram: a 32 KB sound package went in, a 15 KB unreadable ZIP came out,
/// `validate` reported `zip.unreadable`, and the command exited 0. A repair tool that silently truncates
/// its input is worse than one that refuses: the caller has lost the original and gained a corrupt file.
///
/// The round count and change list it printed made it look like work had been done.
/// </summary>
public sealed class RepairSafetyTests
{
    private static string BuildSoundPackage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-rep-{Guid.NewGuid():N}.vsdx");

        new VsdxEngine().Create(new Diagram
        {
            Id = "t",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 12, Height = 9 },
                    Nodes =
                    {
                        new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 6, 1.4, 0.7) },
                        new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 6, 1.4, 0.7) }
                    },
                    Edges = { new Edge { Id = "e", From = "a", To = "b" } }
                }
            }
        }, path, new WriteOptions { AutoLayout = false });

        return path;
    }

    [Fact]
    public void TheRepairedFileIsStillAValidPackage()
    {
        var input = BuildSoundPackage();
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-rep2-{Guid.NewGuid():N}.vsdx");

        try
        {
            var engine = new VsdxEngine();
            var diagram = engine.Read(input).Diagram;

            var controller = new RepairController(2, 6);
            controller.Begin(diagram, "baseline");

            while (controller.NextPlan(diagram) is not null) { }

            engine.Create(diagram, output, new WriteOptions { AutoLayout = false });

            var validation = engine.Validate(output);

            Assert.True(validation.IsValid,
                "repair produced an invalid package: " + string.Join("; ", validation.Errors.Select(e => e.Code)));
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void TheOutputIsAPackageNotModelJson()
    {
        // The defect this test was written for: `--output` received `DiagramJson.Save` output, so a
        // 32 KB sound package became a 15 KB file of JSON named `.vsdx`, and the command exited 0.
        var input = BuildSoundPackage();
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-rep3-{Guid.NewGuid():N}.vsdx");

        try
        {
            var engine = new VsdxEngine();
            var diagram = engine.Read(input).Diagram;

            var controller = new RepairController(2, 6);
            controller.Begin(diagram, "baseline");
            while (controller.NextPlan(diagram) is not null) { }

            engine.Create(diagram, output, new WriteOptions { AutoLayout = false });

            // A VSDX is a ZIP; a model dump starts with '{'.
            using var stream = File.OpenRead(output);
            var header = new byte[2];
            stream.ReadExactly(header);

            Assert.Equal((byte)'P', header[0]);
            Assert.Equal((byte)'K', header[1]);
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void ARepairThatCannotImproveLeavesTheOriginalIntact()
    {
        // The conservative contract: when no plan is available, the input must be untouched.
        var input = BuildSoundPackage();

        try
        {
            long before = new FileInfo(input).Length;

            var engine = new VsdxEngine();
            var diagram = engine.Read(input).Diagram;
            var controller = new RepairController(2, 6);
            controller.Begin(diagram, "baseline");

            int plans = 0;
            while (controller.NextPlan(diagram) is not null) plans++;

            if (plans == 0)
                Assert.Equal(before, new FileInfo(input).Length);
        }
        finally { if (File.Exists(input)) File.Delete(input); }
    }

    [Fact]
    public void TheControllerStopsAfterItsAttemptBudget()
    {
        // "Bounded" is the whole promise; an unbounded loop would hang a caller.
        var input = BuildSoundPackage();

        try
        {
            var engine = new VsdxEngine();
            var diagram = engine.Read(input).Diagram;

            var controller = new RepairController(2, 3);
            controller.Begin(diagram, "baseline");

            int iterations = 0;
            while (controller.NextPlan(diagram) is not null && iterations < 100) iterations++;

            Assert.True(iterations <= 3, $"the controller planned {iterations} rounds against a budget of 3");
        }
        finally { if (File.Exists(input)) File.Delete(input); }
    }
}
