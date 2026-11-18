using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A label position written into the package must come back out.
///
/// The writer stored it in `LabelPlacement`; the reader never restored it. Measured: an edge written
/// with position 0.25 read back as null. That matters because `check-layout` and the post-execute
/// quality check both read from disk — so the one control for separating overlapping labels was
/// invisible to every check that inspects a real file.
///
/// This is the second half of the problem the analyser fix addressed: C9 made the analyser read the
/// field, C11 makes sure the field is actually there to read.
/// </summary>
public sealed class LabelPositionRoundTripTests
{
    private static (Diagram Written, string Path) RoundTrip(double? position)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 6 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 3, 1.4, 0.7) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 3, 1.4, 0.7) }
            },
            Edges =
            {
                new Edge { Id = "e1", From = "a", To = "b", Label = "calls", LabelPosition = position }
            }
        };

        var diagram = new Diagram { Id = "t", Pages = { page } };
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-labelpos-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = false });

        return (new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void APositionSurvivesTheWrite()
    {
        var (written, path) = RoundTrip(0.25);
        try
        {
            var edge = written.Pages[0].Edges.Single(e => e.Id == "e1");

            Assert.NotNull(edge.LabelPosition);
            Assert.Equal(0.25, edge.LabelPosition!.Value, 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnUnsetPositionStaysUnset()
    {
        // Null means "default"; filling it in would make every edge look explicitly placed.
        var (written, path) = RoundTrip(null);
        try
        {
            var edge = written.Pages[0].Edges.Single(e => e.Id == "e1");
            Assert.Null(edge.LabelPosition);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SeveralPositionsRoundTripIndependently()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 6 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 3, 1.4, 0.7) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 3, 1.4, 0.7) }
            },
            Edges =
            {
                new Edge { Id = "e1", From = "a", To = "b", Label = "one", LabelPosition = 0.2 },
                new Edge { Id = "e2", From = "a", To = "b", Label = "two", LabelPosition = 0.8 }
            }
        };

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-labelpos2-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(new Diagram { Id = "t", Pages = { page } }, path,
            new WriteOptions { AutoLayout = false });

        try
        {
            var written = new VsdxEngine().Read(path).Diagram;
            var e1 = written.Pages[0].Edges.Single(e => e.Id == "e1");
            var e2 = written.Pages[0].Edges.Single(e => e.Id == "e2");

            Assert.Equal(0.2, e1.LabelPosition!.Value, 2);
            Assert.Equal(0.8, e2.LabelPosition!.Value, 2);
        }
        finally { File.Delete(path); }
    }
}
