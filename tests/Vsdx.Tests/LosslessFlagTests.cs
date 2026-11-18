using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `lossless` must mean "nothing was lost", not "the engine had something to say".
///
/// It is defined as `Issues.Count == 0`, and `Issues` includes informational notes. A no-op round trip
/// therefore reports `lossless: false` while carrying twelve `info` entries — none of them a loss. The
/// notes are ordinary observations ("wrote 2 text blocks", "restored shape order"), so the flag says
/// "this file lost fidelity" about a file that is byte-identical in everything the caller can see.
///
/// A caller using the flag to decide whether to trust the output gets the wrong answer, and the twelve
/// notes that are genuinely useful get read as evidence of damage.
/// </summary>
public sealed class LosslessFlagTests
{
    private static (Diagram Model, string Path) RoundTrip(Diagram source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-loss-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(source, path, new WriteOptions { AutoLayout = false });

        return (new VsdxEngine().Read(path).Diagram, path);
    }

    private static Diagram Sample() => new()
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
                Edges = { new Edge { Id = "e", From = "a", To = "b" } },
                TextBlocks =
                {
                    new TextBlock { Id = "title", Text = "T", Role = "title", Bounds = new Bounds(6, 8, 4, 0.5) }
                }
            }
        }
    };

    [Fact]
    public void InfoNotesDoNotMakeARoundTripLossy()
    {
        var (model, path) = RoundTrip(Sample());

        try
        {
            var output = Path.Combine(Path.GetTempPath(), $"vsdx-loss2-{Guid.NewGuid():N}.vsdx");

            try
            {
                var result = new VsdxEngine().Update(path, model, output);
                var report = result.Compatibility;

                // Notes are expected — the writer describes what it did.
                Assert.Contains(report.Issues, i => i.Severity == "info");

                // None of them is an error or a warning, so nothing was lost.
                Assert.DoesNotContain(report.Issues, i => i.Severity is "error" or "warning");
                Assert.True(report.Lossless, "a clean round trip reported itself as lossy");
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnActualProblemStillMakesItLossy()
    {
        // The flag must not become vacuous in the other direction.
        var report = new CompatibilityReport();
        report.Warn("write.test", "something was dropped", null);

        Assert.False(report.Lossless);
    }

    [Fact]
    public void AnErrorAlsoMakesItLossy()
    {
        var report = new CompatibilityReport();
        report.Error("write.test", "something failed", null);

        Assert.False(report.Lossless);
    }

    [Fact]
    public void AnEmptyReportIsLossless()
    {
        Assert.True(new CompatibilityReport().Lossless);
    }
}
