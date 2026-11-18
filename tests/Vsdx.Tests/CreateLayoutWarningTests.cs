using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Creating a file must not silently produce a broken layout.
///
/// `create` reported success (exit 0, compatibility lossless) on a model whose containers
/// overlapped by 10.85 inches — a diagram that scores 0/100 and renders with bands on top of
/// each other. The caller had no way to know without running a separate check-layout pass,
/// which nothing told them to do.
///
/// The fix reports hard layout problems through the same compatibility channel used for every
/// other write-time issue. Writing still succeeds: the file is real and may be worth inspecting,
/// and refusing to write would discard work. But it can no longer claim to be clean.
/// </summary>
public sealed class CreateLayoutWarningTests
{
    /// <summary>Two containers whose authored bounds overlap — the shape that produced the bug.</summary>
    private static Diagram OverlappingBands()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 14, Height = 9 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 6.0, 1.6, 0.8), GroupId = "g1" },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(3, 5.5, 1.6, 0.8), GroupId = "g2" }
            },
            Groups =
            {
                new Group { Id = "g1", Name = "Band 1", Members = { "a" }, Bounds = new Bounds(3, 6.0, 3, 2.0) },
                new Group { Id = "g2", Name = "Band 2", Members = { "b" }, Bounds = new Bounds(3, 5.5, 3, 2.0) }
            }
        };

        return new Diagram { Pages = { page } };
    }

    private static string WriteToTemp(Diagram diagram, out WriteResult result)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-layout-warn-{Guid.NewGuid():N}.vsdx");
        var engine = new VsdxEngine();
        result = engine.Create(diagram, path, new WriteOptions { AutoLayout = false });
        return path;
    }

    [Fact]
    public void OverlappingContainersAreReportedNotSwallowed()
    {
        var path = WriteToTemp(OverlappingBands(), out var result);
        try
        {
            var codes = result.Compatibility.Issues.Select(i => i.Code).ToList();

            Assert.Contains(codes, c => c.StartsWith("layout.", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OverlapIsAWarningNotASilentSuccess()
    {
        var path = WriteToTemp(OverlappingBands(), out var result);
        try
        {
            // The file is still written — refusing would throw away the work.
            Assert.True(File.Exists(path));
            Assert.True(result.ShapesWritten > 0);

            // But the report must say something is wrong.
            Assert.NotEmpty(result.Compatibility.Issues);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ACleanModelGainsNoSpuriousLayoutIssues()
    {
        // The warning must fire on real problems, not on every write. This model is deliberately
        // well-formed: the page is sized to its content, the two nodes are apart, nothing overlaps.
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 6, Height = 3 },
            Nodes =
            {
                new Node { Id = "a", Text = "A", Bounds = new Bounds(1.5, 1.5, 1.6, 0.8) },
                new Node { Id = "b", Text = "B", Bounds = new Bounds(4.2, 1.5, 1.6, 0.8) }
            },
            Edges = { new Edge { Id = "ab", From = "a", To = "b" } }
        };
        var diagram = new Diagram { Pages = { page } };

        var path = WriteToTemp(diagram, out var result);
        try
        {
            var layoutIssues = result.Compatibility.Issues
                .Where(i => i.Code.StartsWith("layout.", StringComparison.Ordinal))
                .ToList();

            Assert.Empty(layoutIssues);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
