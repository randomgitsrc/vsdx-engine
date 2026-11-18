using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Clipped text is not a deliverable.
///
/// A run at baseSize 24 produced twelve overflow reports and still returned
/// `hardConstraintsPassed: true`, `score: 81`, `status: succeeded`. Both entry points agreed —
/// `check-layout` classified them as warnings too — so the two were consistently wrong rather
/// than inconsistently measured.
///
/// Text that does not fit its box loses content. That is not a matter of taste the way spacing or
/// alignment is, and treating it as advisory makes the project's own acceptance item
/// ("不发生文本裁切") impossible to falsify: whatever the overlap, the answer was yes.
/// </summary>
public sealed class TextOverflowSeverityTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// A node whose label cannot fit, built directly rather than via IR.
    ///
    /// IR-side sizing kept growing the box to fit the text, so the overflow never happened and a
    /// conditional test would have passed without asserting anything. The analyser has to be
    /// handed the geometry that actually clips.
    /// </summary>
    private static Diagram Clipped()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 6, Height = 4 },
            Nodes =
            {
                new Node
                {
                    Id = "a",
                    Text = "A very long label that cannot fit",
                    Bounds = new Bounds(3, 2, 1.2, 0.4),
                    Style = new ShapeStyle { FontSize = 18 }
                }
            }
        };

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void TheFixtureActuallyClips()
    {
        // Guard on the guard: if this stops holding, the three tests below would pass vacuously.
        var report = LayoutAnalyzer.Analyze(Clipped());

        Assert.Contains(report.Issues, i => i.Code.StartsWith("text.overflow", StringComparison.Ordinal));
    }

    [Fact]
    public void ClippedTextFailsTheHardConstraints()
    {
        var report = LayoutAnalyzer.Analyze(Clipped());

        Assert.Contains(report.Issues, i => i.Code.StartsWith("text.overflow", StringComparison.Ordinal));
        Assert.False(report.HardConstraintsPassed, "clipping reported but hard constraints still pass");
    }

    [Fact]
    public void AnOverflowIssueIsAnError()
    {
        var report = LayoutAnalyzer.Analyze(Clipped());

        var overflow = report.Issues
            .Where(i => i.Code.StartsWith("text.overflow", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(overflow);
        Assert.All(overflow, i => Assert.Equal("error", i.Severity));
    }

    [Fact]
    public void ScoreIsZeroWhenTextIsClipped()
    {
        var report = LayoutAnalyzer.Analyze(Clipped());

        Assert.Contains(report.Issues, i => i.Code.StartsWith("text.overflow", StringComparison.Ordinal));
        Assert.Equal(0, report.Score);
    }

    [Fact]
    public void AHealthyDiagramIsUnaffected()
    {
        // The change must not fail diagrams that fit. Two short labels on a sized page.
        var diagram = new Diagram
        {
            Id = "ok",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 8, Height = 4 },
                    Nodes =
                    {
                        new Node { Id = "a", Text = "A", Bounds = new Bounds(2.5, 2, 2, 1) },
                        new Node { Id = "b", Text = "B", Bounds = new Bounds(5.5, 2, 2, 1) }
                    },
                    Edges = { new Edge { Id = "e", From = "a", To = "b" } }
                }
            }
        };

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.DoesNotContain(report.Issues, i => i.Code.StartsWith("text.overflow", StringComparison.Ordinal));
        Assert.True(report.HardConstraintsPassed);
    }
}
