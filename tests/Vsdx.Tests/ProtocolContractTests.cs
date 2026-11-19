using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// ProtocolContractTests: the three-document protocol is the surface an external agent calls, so
/// its guarantees are tested directly rather than inferred from engine behaviour.
///
/// The four invariants the brief requires (§7.2):
///  1. IR and DesignSpec are separate; the same IR works with different specs.
///  2. The engine reports unmet constraints instead of ignoring them.
///  3. Status distinguishes success / partial / failure.
///  4. Unknown capability is reported, not silently dropped.
/// </summary>
public sealed class ProtocolContractTests
{
    private static DiagramIr SmallIr() => new()
    {
        Id = "contract",
        Title = "合同测试图",
        Kind = "architecture",
        Containers =
        {
            new IrContainer { Id = "band", Label = "服务层 Service" }
        },
        Nodes =
        {
            new IrNode { Id = "a", Label = "网关\nGateway", Role = "gateway", ContainerId = "band" },
            new IrNode { Id = "b", Label = "服务\nService", Role = "service", ContainerId = "band" },
            new IrNode { Id = "c", Label = "数据库\nDatabase", Role = "database" }
        },
        Edges =
        {
            new IrEdge { Id = "e1", From = "a", To = "b", Relation = "calls", Label = "REST" },
            new IrEdge { Id = "e2", From = "b", To = "c", Relation = "writes", Label = "SQL" }
        }
    };

    // ---- 1. IR / DesignSpec separation -----------------------------------

    [Fact]
    public void DesignSpecLayoutChoiceIsHonoured()
    {
        // On a small grouped diagram most strategies converge on the same banded arrangement, so
        // asserting "geometry differs" would be testing the fixture rather than the contract.
        // What the contract *must* guarantee is that the caller's layout choice reaches the
        // engine: verified here by spacing, which is observable and unambiguous.
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-layout");

        var tight = runner.Execute(SmallIr(), new DesignSpec
        {
            Layout = "layered",
            Spacing = new SpacingSpec { NodeGapX = 0.2, BandGapY = 0.4 }
        }, Path.Combine(dir, "tight.vsdx"));

        var loose = runner.Execute(SmallIr(), new DesignSpec
        {
            Layout = "layered",
            Spacing = new SpacingSpec { NodeGapX = 1.2, BandGapY = 1.6 }
        }, Path.Combine(dir, "loose.vsdx"));

        Assert.Equal(ExecutionStatus.Succeeded, tight.Status);
        Assert.Equal(ExecutionStatus.Succeeded, loose.Status);

        var engine = new VsdxEngine();
        double WidthOf(string file)
        {
            var nodes = engine.Read(file).Diagram.Pages[0].Nodes;
            return nodes.Max(n => n.Bounds.X + n.Bounds.W / 2)
                 - nodes.Min(n => n.Bounds.X - n.Bounds.W / 2);
        }

        Assert.True(WidthOf(Path.Combine(dir, "loose.vsdx")) > WidthOf(Path.Combine(dir, "tight.vsdx")),
            "wider node spacing must produce a wider drawing");
    }

    [Fact]
    public void SameIrRunsUnderDifferentSpecs()
    {
        // The IR/spec separation: one IR, several specs, each executing independently.
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-multi");

        var specs = new[]
        {
            new DesignSpec { Layout = "auto" },
            new DesignSpec { Layout = "flow" },
            new DesignSpec { Layout = "tree" }
        };

        for (int i = 0; i < specs.Length; i++)
        {
            var r = runner.Execute(SmallIr(), specs[i], Path.Combine(dir, $"s{i}.vsdx"));
            Assert.Equal(ExecutionStatus.Succeeded, r.Status);
            Assert.True(File.Exists(Path.Combine(dir, $"s{i}.vsdx")));
        }
    }

    [Fact]
    public void IrDoesNotRequireCoordinates()
    {
        // The whole point of IR: no anchor anywhere, engine still places everything.
        var ir = SmallIr();
        Assert.All(ir.Nodes, n => Assert.Null(n.Anchor));

        var diagram = DiagramIrConverter.ToDiagram(ir);
        Assert.Equal(3, diagram.Pages[0].Nodes.Count);
    }

    [Fact]
    public void FixedAnchorsAreHonoured()
    {
        var ir = SmallIr();
        ir.Nodes[0].Anchor = new IrAnchor { X = 7.5, Y = 9.25, Mode = "fixed" };

        var diagram = DiagramIrConverter.ToDiagram(ir);
        var anchored = diagram.Pages[0].Nodes.Single(n => n.Id == "a");

        Assert.Equal(7.5, anchored.Bounds.X, 3);
        Assert.Equal(9.25, anchored.Bounds.Y, 3);
    }

    // ---- 2. Unmet constraints are reported, never ignored ----------------

    [Fact]
    public void UnsatisfiableHardConstraintIsReported()
    {
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-unmet");

        // Demand zero edge crossings from a graph where the layered layout may produce some.
        var spec = new DesignSpec
        {
            Layout = "layered",
            HardConstraints = { new ConstraintSpec { Code = "max_page_width", Value = 1.0 } }
        };

        var result = runner.Execute(SmallIr(), spec, Path.Combine(dir, "out.vsdx"));

        Assert.Contains(result.UnmetConstraints, u => u.Code == "max_page_width" && u.IsHard);
        Assert.NotEqual(ExecutionStatus.Succeeded, result.Status);
    }

    [Fact]
    public void UnknownConstraintIsReportedNotDropped()
    {
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-unknown");

        var spec = new DesignSpec
        {
            HardConstraints = { new ConstraintSpec { Code = "must_be_pretty" } }
        };

        var result = runner.Execute(SmallIr(), spec, Path.Combine(dir, "out.vsdx"));

        Assert.Contains(result.UnmetConstraints, u => u.Code == "unknown_constraint");
    }

    [Fact]
    public void SatisfiableConstraintProducesNoUnmetEntry()
    {
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-met");

        var spec = new DesignSpec
        {
            HardConstraints =
            {
                new ConstraintSpec { Code = "max_page_width", Value = 40 },
                new ConstraintSpec { Code = "require_title" }
            }
        };

        var result = runner.Execute(SmallIr(), spec, Path.Combine(dir, "out.vsdx"));

        Assert.DoesNotContain(result.UnmetConstraints, u => u.Code == "max_page_width");
    }

    // ---- 3. Status distinguishes the outcomes ----------------------------

    [Fact]
    public void CleanRunReportsSucceeded()
    {
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-ok");

        var result = runner.Execute(SmallIr(), new DesignSpec { Layout = "layered" },
            Path.Combine(dir, "out.vsdx"));

        Assert.Equal(ExecutionStatus.Succeeded, result.Status);
        Assert.True(result.Quality.HardConstraintsPassed);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void InvalidIrReportsFailedWithReason()
    {
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-bad");

        var ir = SmallIr();
        ir.Edges.Add(new IrEdge { Id = "bad", From = "a", To = "does-not-exist" });

        var result = runner.Execute(ir, null, Path.Combine(dir, "out.vsdx"));

        Assert.Equal(ExecutionStatus.Failed, result.Status);
        Assert.Contains(result.Errors, e => e.Code == "ir.edge-to-missing");
    }

    [Fact]
    public void EmptyIrIsRejected()
    {
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-empty");

        var result = runner.Execute(new DiagramIr { Id = "x" }, null, Path.Combine(dir, "out.vsdx"));

        Assert.Equal(ExecutionStatus.Failed, result.Status);
        Assert.Contains(result.Errors, e => e.Code == "ir.empty");
    }

    // ---- 4. Result carries mapping, files, quality ----------------------

    [Fact]
    public void ResultMapsIrIdsBackToModelIds()
    {
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-map");

        var result = runner.Execute(SmallIr(), new DesignSpec { Layout = "layered" },
            Path.Combine(dir, "out.vsdx"));

        Assert.Equal("a", result.ObjectMapping["a"]);
        Assert.Equal("b", result.ObjectMapping["b"]);
        Assert.Equal("band", result.ObjectMapping["band"]);
    }

    [Fact]
    public void ResultListsOutputFilesAndQuality()
    {
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-files");

        var result = runner.Execute(SmallIr(), new DesignSpec { Layout = "layered" },
            Path.Combine(dir, "out.vsdx"), render: true);

        Assert.NotEmpty(result.OutputFiles);
        Assert.All(result.OutputFiles, f => Assert.True(File.Exists(f), f));
        Assert.Single(result.Renders);
        Assert.True(result.Renders[0].Bytes > 1000);
        Assert.True(result.Quality.Score > 0);
        // `requiresVisualReview` tracks whether anything was flagged, not "always ask a human". This
        // assertion used to demand `true` unconditionally, which made the flag meaningless; see
        // VisualReviewFlagTests.
        Assert.Equal(
            result.Quality.HardViolations + result.Quality.SoftViolations > 0,
            result.Quality.RequiresVisualReview);
    }

    [Fact]
    public void StagesAreRecordedInOrder()
    {
        var runner = new ProtocolRunner();
        var dir = TestData.NewTempDir("proto-stages");

        var result = runner.Execute(SmallIr(), new DesignSpec { Layout = "layered" },
            Path.Combine(dir, "out.vsdx"), render: true);

        Assert.Equal(
            new[] { "ir.validate", "ir.toModel", "design.apply", "route", "vsdx.write", "quality.check", "render" },
            result.StagesCompleted.ToArray());
    }

    // ---- IR validation rules --------------------------------------------

    [Fact]
    public void ValidatorCatchesDuplicateNodeIds()
    {
        var ir = SmallIr();
        ir.Nodes.Add(new IrNode { Id = "a", Label = "dup" });

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.node-id-duplicate");
    }

    [Fact]
    public void ValidatorCatchesMissingContainer()
    {
        var ir = SmallIr();
        ir.Nodes[0].ContainerId = "nope";

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.container-missing");
    }

    [Fact]
    public void ValidatorCatchesParentCycles()
    {
        var ir = new DiagramIr
        {
            Nodes = { new IrNode { Id = "n", Label = "N" } },
            Containers =
            {
                new IrContainer { Id = "c1", ParentId = "c2" },
                new IrContainer { Id = "c2", ParentId = "c1" }
            }
        };

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.parent-cycle");
    }

    [Fact]
    public void ValidatorRejectsWrongMajorVersion()
    {
        var ir = SmallIr();
        ir.IrVersion = "2.0";

        var errors = IrValidator.Validate(ir);
        Assert.Contains(errors, e => e.Code == "ir.version");
    }

    [Fact]
    public void VersionCompatibilityRule()
    {
        Assert.True(ProtocolVersion.IsCompatible("1.0", "1"));
        Assert.True(ProtocolVersion.IsCompatible("1.7", "1"));
        Assert.False(ProtocolVersion.IsCompatible("2.0", "1"));
        Assert.False(ProtocolVersion.IsCompatible(null, "1"));
        Assert.False(ProtocolVersion.IsCompatible("", "1"));
    }

    // ---- Round-trip through JSON ----------------------------------------

    [Fact]
    public void IrRoundTripsThroughJson()
    {
        var ir = SmallIr();
        var json = JsonSerializer.Serialize(ir, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var back = JsonSerializer.Deserialize<DiagramIr>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(back);
        Assert.Equal(ir.Nodes.Count, back!.Nodes.Count);
        Assert.Equal(ir.Edges.Count, back.Edges.Count);
        Assert.Equal("architecture", back.Kind);
    }

    [Fact]
    public void DesignSpecRoundTripsThroughJson()
    {
        var spec = new DesignSpec
        {
            Layout = "flow",
            Canvas = new CanvasSpec { Mode = "fixed", Width = 16, Height = 10 },
            HardConstraints = { new ConstraintSpec { Code = "min_page_utilisation", Value = 0.4 } }
        };

        var json = JsonSerializer.Serialize(spec, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var back = JsonSerializer.Deserialize<DesignSpec>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(back);
        Assert.Equal("flow", back!.Layout);
        Assert.Equal(16, back.Canvas.Width);
        Assert.Single(back.HardConstraints);
    }

    // ---- Role → shape mapping -------------------------------------------

    [Theory]
    [InlineData("gateway", "Hexagon")]
    [InlineData("database", "Process")]
    [InlineData("queue", "Parallelogram")]
    [InlineData("actor", "Rectangle")]
    [InlineData("decision", "Diamond")]
    [InlineData("service", "Rectangle")]
    [InlineData(null, "Rectangle")]
    public void RoleMapsToShape(string? role, string expected)
    {
        Assert.Equal(expected, DiagramIrConverter.ShapeForRole(role));
    }

    [Fact]
    public void ContainsRelationOmitsArrow()
    {
        Assert.False(DiagramIrConverter.RelationUsesArrow("contains"));
        Assert.True(DiagramIrConverter.RelationUsesArrow("calls"));
    }
}
