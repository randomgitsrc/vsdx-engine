using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A `FreePath` in the JSON model must reach the package, including on a page that has nothing else on it.
///
/// It did not. `VisioFreePathBridge` bailed out when the page had no `<Shapes>` element, which is exactly what
/// an otherwise-empty page part looks like — the writer emits `<PageContents/>` with nothing inside. So a
/// model whose only content was a custom outline produced **zero shapes**, and the run reported `lossless`
/// because nothing had been recorded as dropped.
///
/// `VisioTextBlockBridge` had the same shape of bug and was fixed the same way: create the container rather
/// than treat its absence as "nothing to do".
///
/// The round-60 test that confirmed path fidelity goes through the IR, which lays out nodes first, so the page
/// already had shapes by the time the path was written. This path was never covered.
/// </summary>
public sealed class FreePathWriteTests
{
    private static Diagram PathOnly() => new()
    {
        Id = "t",
        Pages =
        {
            new Page
            {
                Name = "p",
                Size = new PageSize { Width = 10, Height = 8 },
                Paths =
                {
                    new FreePath
                    {
                        Id = "p1",
                        Bounds = new Bounds(7, 2, 2, 1),
                        Geometry =
                        {
                            new PathGeometry
                            {
                                Index = 0,
                                Rows =
                                {
                                    new PathRow { Type = "MoveTo", Cells = new Dictionary<string, string> { ["X"] = "0", ["Y"] = "0" } },
                                    new PathRow { Type = "LineTo", Cells = new Dictionary<string, string> { ["X"] = "2", ["Y"] = "1" } }
                                }
                            }
                        }
                    }
                }
            }
        }
    };

    private static string Temp() => Path.Combine(Path.GetTempPath(), $"vsdx-fp-{Guid.NewGuid():N}.vsdx");

    [Fact]
    public void APathOnAnOtherwiseEmptyPageIsWritten()
    {
        var path = Temp();

        try
        {
            new VsdxEngine().Create(PathOnly(), path, new WriteOptions { AutoLayout = false });

            // `ShapesWritten` counts what OfficeIMO wrote; paths are patched in afterwards by the bridge, so
            // the read-back is the only honest check.
            var back = new VsdxEngine().Read(path).Diagram;

            Assert.Single(back.Pages[0].Paths);
            Assert.Equal("p1", back.Pages[0].Paths[0].Id);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TheGeometrySurvivesWithItsCoordinates()
    {
        var path = Temp();

        try
        {
            new VsdxEngine().Create(PathOnly(), path, new WriteOptions { AutoLayout = false });
            var back = new VsdxEngine().Read(path).Diagram;

            var rows = back.Pages[0].Paths[0].Geometry[0].Rows!;

            Assert.Equal(2, rows.Count);
            Assert.Equal("MoveTo", rows[0].Type);
            Assert.Equal("2", rows[1].Cells!["X"]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void APathBesideNodesStillWorks()
    {
        // The case the IR covers, kept so the fix does not regress it.
        var diagram = PathOnly();
        diagram.Pages[0].Nodes.Add(new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 6, 1.3, 0.6) });

        var path = Temp();

        try
        {
            new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = false });
            var back = new VsdxEngine().Read(path).Diagram;

            Assert.Single(back.Pages[0].Paths);
            Assert.Single(back.Pages[0].Nodes);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ADroppedPathWouldNotBeReportedAsLossless()
    {
        // The second half of the defect: nothing said the path was gone.
        var path = Temp();

        try
        {
            var result = new VsdxEngine().Create(PathOnly(), path, new WriteOptions { AutoLayout = false });

            bool wroteShape = new VsdxEngine().Read(path).Diagram.Pages[0].Paths.Count == 1;
            bool reported = result.Compatibility.Issues.Any(i => i.Code == "write.free-path");

            Assert.True(wroteShape || reported,
                "the path was neither written nor reported");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
