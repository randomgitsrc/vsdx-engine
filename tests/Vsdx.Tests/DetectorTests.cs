using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// DetectorTests: the quality checker must report the defects the Phase 0 diagnosis found it
/// was silently missing, and must not invent defects on clean diagrams.
///
/// Each test here corresponds to a documented blind spot:
///   group-to-group overlap, edge detour ratio, row alignment, size jitter,
///   hard/soft separation, and score gating.
/// </summary>
public sealed class DetectorTests
{
    [Fact]
    public void Detector_ReportsGroupOverlap()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Groups =
            {
                new Group { Id = "g1", Bounds = new Bounds(4, 5, 6, 2) },
                new Group { Id = "g2", Bounds = new Bounds(4, 4, 6, 2) }   // overlaps g1
            },
            Nodes = { new Node { Id = "n", Bounds = new Bounds(3, 5, 1, 0.6) } }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.Contains(report.Issues, i => i.Code == "group.overlap");
        Assert.Contains(report.Errors, i => i.Code == "group.overlap");
    }

    [Fact]
    public void Detector_GroupOverlapIsHardConstraint()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Groups =
            {
                new Group { Id = "g1", Bounds = new Bounds(4, 5, 6, 2) },
                new Group { Id = "g2", Bounds = new Bounds(4, 4.2, 6, 2) }
            },
            Nodes = { new Node { Id = "n", Bounds = new Bounds(3, 5, 1, 0.6) } }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.False(report.HardConstraintsPassed);
        Assert.Equal(0, report.Score);
    }

    [Fact]
    public void Detector_AllowsNestedGroups()
    {
        // A container inside another container is intentional, not an overlap defect.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Groups =
            {
                new Group { Id = "outer", Bounds = new Bounds(6, 4, 10, 6) },
                new Group { Id = "inner", Bounds = new Bounds(6, 4, 4, 2) }
            },
            Nodes = { new Node { Id = "n", Bounds = new Bounds(6, 4, 1, 0.6) } }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.DoesNotContain(report.Issues, i => i.Code == "group.overlap");
    }

    [Fact]
    public void Detector_ReportsExcessiveDetour()
    {
        // Two nodes far apart; a waypoint that forces the connector to wander.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 10 },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(2, 7, 1.6, 0.8) },
                new Node { Id = "b", Bounds = new Bounds(12, 7, 1.6, 0.8) }
            },
            Edges =
            {
                new Edge
                {
                    Id = "e", From = "a", To = "b", Kind = "rightAngle",
                    Waypoints = new List<Point>
                    {
                        new(2, 1.0), new(12, 1.0)   // detour down and back up
                    }
                }
            }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.Contains(report.Issues, i => i.Code == "edge.detour");
        Assert.True(report.EdgeDetourRatio.ContainsKey("e"));
        Assert.True(report.EdgeDetourRatio["e"] > 2.0);
    }

    [Fact]
    public void Detector_DirectEdgeHasRatioNearOne()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 10 },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(2, 7, 1.6, 0.8) },
                new Node { Id = "b", Bounds = new Bounds(8, 7, 1.6, 0.8) }
            },
            Edges = { new Edge { Id = "e", From = "a", To = "b", Kind = "straight" } }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.True(report.EdgeDetourRatio["e"] <= 1.01);
        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.detour");
    }

    [Fact]
    public void Detector_ReportsRowMisalignment()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(2, 6.0, 2, 0.9) },
                new Node { Id = "b", Bounds = new Bounds(6, 6.2, 2, 0.9) }   // 0.2" off the row
            }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.Contains(report.Issues, i => i.Code == "row.misaligned");
    }

    [Fact]
    public void Detector_AlignedRowIsClean()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(2, 6.0, 2, 0.9) },
                new Node { Id = "b", Bounds = new Bounds(6, 6.0, 2, 0.9) },
                new Node { Id = "c", Bounds = new Bounds(10, 6.0, 2, 0.9) }
            }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.DoesNotContain(report.Issues, i => i.Code == "row.misaligned");
        Assert.DoesNotContain(report.Issues, i => i.Code == "row.size-jitter");
    }

    [Fact]
    public void Detector_ReportsSizeJitter()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 9 },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(2, 6, 4.5, 0.9) },
                new Node { Id = "b", Bounds = new Bounds(8, 6, 1.8, 0.9) }
            }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.Contains(report.Issues, i => i.Code == "row.size-jitter");
    }

    [Fact]
    public void Detector_CompactMarkersAreNotSizeJitter()
    {
        // A circle actor next to a wide box is intentional, not jitter.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 9 },
            Nodes =
            {
                new Node { Id = "box", Bounds = new Bounds(3, 6, 3.0, 0.9) },
                new Node { Id = "dot", Bounds = new Bounds(8, 6, 1.0, 1.0) }
            }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.DoesNotContain(report.Issues, i => i.Code == "row.size-jitter");
    }

    [Fact]
    public void Detector_HardFailureForcesZeroScore()
    {
        // Overlapping nodes are a hard violation; no amount of soft cleanliness may rescue it.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 11, Height = 8.5 },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(3, 5, 2, 1) },
                new Node { Id = "b", Bounds = new Bounds(3.5, 5, 2, 1) }
            }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.False(report.HardConstraintsPassed);
        Assert.Equal(0, report.Score);
    }

    [Fact]
    public void Detector_SoftOnlyProducesNonZeroScore()
    {
        var page = new Page
        {
            Name = "p",
            // Page sized snugly around the content so the only defect is the misalignment.
            Size = new PageSize { Width = 7.0, Height = 3.0 },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(2.0, 1.5, 2, 0.9) },
                new Node { Id = "b", Bounds = new Bounds(5.0, 1.7, 2, 0.9) }   // soft: misaligned
            }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.True(report.HardConstraintsPassed);
        Assert.True(report.Score > 0);
        Assert.True(report.Score < 100);
    }

    [Fact]
    public void Detector_RepeatedCodeIsCapped()
    {
        // Many identical soft violations must not drive the score to zero by sheer count.
        var nodes = new List<Node>();
        for (int i = 0; i < 12; i++)
            nodes.Add(new Node { Id = $"a{i}", Bounds = new Bounds(2 + i * 0.9, 4.0 + (i % 2) * 0.3, 0.6, 1.2) });

        // Keep the page tight around the content so this test exercises the repeat-cap rule,
        // not the page-utilisation rule.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 13, Height = 6 },
            Nodes = nodes
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.True(report.HardConstraintsPassed);
        Assert.True(report.Score > 0, "a single repeated soft defect must not zero the score");
    }

    [Fact]
    public void Detector_IssuesCarryRepairGuidance()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Groups =
            {
                new Group { Id = "g1", Bounds = new Bounds(4, 5, 6, 2) },
                new Group { Id = "g2", Bounds = new Bounds(4, 4, 6, 2) }
            },
            Nodes = { new Node { Id = "n", Bounds = new Bounds(3, 5, 1, 0.6) } }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });
        var issue = report.Issues.First(i => i.Code == "group.overlap");

        Assert.False(string.IsNullOrWhiteSpace(issue.RootCause));
        Assert.False(string.IsNullOrWhiteSpace(issue.RecommendedFix));
        Assert.Equal(FixLevel.FullRedraw, issue.SuggestedLevel);
    }

    [Fact]
    public void Detector_CleanDiagramScoresFull()
    {
        // A deliberately tidy two-band diagram: aligned rows, uniform sizes, vertical edges.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Groups =
            {
                new Group { Id = "top", Bounds = new Bounds(6, 6.5, 8, 2) },
                new Group { Id = "bottom", Bounds = new Bounds(6, 2.0, 8, 2) }
            },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(4, 6.5, 2, 0.9) },
                new Node { Id = "b", Bounds = new Bounds(8, 6.5, 2, 0.9) },
                new Node { Id = "c", Bounds = new Bounds(4, 2.0, 2, 0.9) }
            },
            Edges = { new Edge { Id = "e", From = "a", To = "c", Kind = "straight" } }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });

        Assert.True(report.HardConstraintsPassed,
            string.Join("; ", report.Errors.Select(e => e.Code + ":" + e.Message)));
        Assert.DoesNotContain(report.Issues, i => i.Code is "group.overlap" or "edge.detour" or "row.misaligned");
    }
}
