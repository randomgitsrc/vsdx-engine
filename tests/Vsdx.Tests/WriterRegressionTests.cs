using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The three writer defects fixed in rounds 100–102 stay fixed, checked through the API rather than the CLI.
///
/// They were found by following one bug into its neighbours: a free path dropped on an empty page (the bridge
/// treated a missing `<Shapes>` as "nothing to do"), a background colour dropped when unparseable and written
/// as text when parseable, and a metadata exception swallowed whole. Each is silent by nature — the output is
/// valid, just missing something the caller asked for.
///
/// **One reader trap worth recording**: `create --json` has no `warnings` array. Compatibility issues live
/// under `compatibility.issues`. I checked the wrong field, saw an empty list, and briefly concluded the
/// background fix had not worked.
/// </summary>
public sealed class WriterRegressionTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), $"vsdx-wr-{Guid.NewGuid():N}.vsdx");

    [Fact]
    public void APathOnAnEmptyPageSurvives()
    {
        var diagram = new Diagram
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
                            Bounds = new Bounds(5, 4, 2, 1),
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

        var path = Temp();

        try
        {
            new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = false });

            Assert.Single(new VsdxEngine().Read(path).Diagram.Pages[0].Paths);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AnUnparseableBackgroundIsReported()
    {
        var path = Temp();

        try
        {
            var result = new VsdxEngine().Create(Basic("#GGG"), path, new WriteOptions { AutoLayout = false });

            Assert.Contains(result.Compatibility.Issues, i => i.Code == "write.background-unparsed");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AParsedBackgroundIsWrittenAsHex()
    {
        var path = Temp();

        try
        {
            new VsdxEngine().Create(Basic("rgb(999,0,0)"), path, new WriteOptions { AutoLayout = false });

            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            using var reader = new StreamReader(zip.Entries
                .First(e => e.FullName == "visio/pages/pages.xml").Open());
            var xml = reader.ReadToEnd();

            Assert.Contains("#FF0000", xml);
            Assert.DoesNotContain("rgb(", xml);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AValidBackgroundProducesNoComplaint()
    {
        var path = Temp();

        try
        {
            var result = new VsdxEngine().Create(Basic("#00AA00"), path, new WriteOptions { AutoLayout = false });

            Assert.DoesNotContain(result.Compatibility.Issues, i => i.Code == "write.background-unparsed");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Diagram Basic(string? background) => new()
    {
        Id = "t",
        Pages =
        {
            new Page
            {
                Name = "p",
                Size = new PageSize { Width = 6, Height = 5 },
                Background = background,
                Nodes =
                {
                    new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 2, 1.6, 0.7) },
                    new Node { Id = "b", Text = "B", Bounds = new Bounds(4, 3.5, 1.6, 0.7) }
                },
                Edges = { new Edge { Id = "e", From = "a", To = "b" } }
            }
        }
    };
}
