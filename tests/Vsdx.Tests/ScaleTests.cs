using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Large inputs must still produce a valid file, not merely fail slowly.
///
/// The independent review reported that 5000 nodes timed out with no output. Re-measured on this
/// machine it completes in 33 s with a 14.7 MB package, so that claim did not reproduce — but the
/// review was right that nothing pinned the behaviour, which is why it could not be settled either way.
///
/// These are correctness tests, not timing tests: wall-clock assertions are unstable across machines
/// and would fail for reasons unrelated to the engine. What matters is that size does not corrupt the
/// result. Measured cost is about 5 ms per node, near-linear; recorded in docs/phase8/scale-benchmark.md.
/// </summary>
public sealed class ScaleTests
{
    private static Diagram Ring(int count)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 40, Height = 40 }
        };

        for (int i = 0; i < count; i++)
            page.Nodes.Add(new Node { Id = $"n{i}", Text = $"N{i}" });

        for (int i = 0; i < count; i++)
            page.Edges.Add(new Edge { Id = $"e{i}", From = $"n{i}", To = $"n{(i + 1) % count}" });

        return new Diagram { Id = "big", Type = "architecture", Pages = { page } };
    }

    [Theory]
    [InlineData(500)]
    [InlineData(2000)]
    public void ALargeDiagramIsWrittenAndReadBackIntact(int count)
    {
        var diagram = Ring(count);
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-scale-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = true });

            Assert.True(new FileInfo(path).Length > 0, "the package is empty");

            var back = new VsdxEngine().Read(path).Diagram.Pages[0];

            Assert.Equal(count, back.Nodes.Count);
            Assert.Equal(count, back.Edges.Count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void NodeIdsSurviveAtScale()
    {
        // Identity is the property a caller depends on; a package that renames under load is useless
        // no matter how fast it writes.
        const int count = 800;
        var diagram = Ring(count);
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-scale2-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = true });

            var back = new VsdxEngine().Read(path).Diagram.Pages[0];
            var expected = Enumerable.Range(0, count).Select(i => $"n{i}").ToHashSet(StringComparer.Ordinal);

            Assert.All(back.Nodes, n => Assert.Contains(n.Id, expected));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ALargePackageStillValidates()
    {
        var diagram = Ring(600);
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-scale3-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = true });

            var validation = new VsdxEngine().Validate(path);

            Assert.Empty(validation.Errors);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
