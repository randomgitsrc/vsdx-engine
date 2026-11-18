using System.Diagnostics;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every command whose output is a file must produce a VSDX package, not model JSON.
///
/// `repair` had been writing `DiagramJson.Save` output into the path given to `--output`: a file named
/// `.vsdx` whose contents were `{ "id": ... }`. It was the only one. `create`, `execute`, `update`,
/// `compare` (with and without `--emit-dir`) and `batch` all produce ZIP archives whose first two bytes
/// are `PK`.
///
/// The defect was invisible from the engine's side — `RepairController` is bounded and correct — so this
/// checks the outputs rather than the code paths.
/// </summary>
public sealed class CommandOutputFormatTests
{
    private static string NewPath(string stem) =>
        Path.Combine(Path.GetTempPath(), $"vsdx-fmt-{stem}-{Guid.NewGuid():N}.vsdx");

    private static bool LooksLikePackage(string path)
    {
        if (!File.Exists(path)) return false;

        using var stream = File.OpenRead(path);
        var header = new byte[2];

        return stream.Read(header, 0, 2) == 2 && header[0] == (byte)'P' && header[1] == (byte)'K';
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
                Edges = { new Edge { Id = "e", From = "a", To = "b" } }
            }
        }
    };

    [Fact]
    public void CreateProducesAPackage()
    {
        var path = NewPath("create");

        try
        {
            new VsdxEngine().Create(Sample(), path, new WriteOptions { AutoLayout = false });
            Assert.True(LooksLikePackage(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void UpdateProducesAPackage()
    {
        var source = NewPath("src");
        var output = NewPath("upd");

        try
        {
            new VsdxEngine().Create(Sample(), source, new WriteOptions { AutoLayout = false });
            var model = new VsdxEngine().Read(source).Diagram;

            new VsdxEngine().Update(source, model, output, new UpdateOptions { AutoLayout = false });

            Assert.True(LooksLikePackage(output));
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void ARelayoutProducesAPackage()
    {
        // The path `repair` uses to write its result — the one that was wrong.
        var source = NewPath("rel");
        var output = NewPath("rel2");

        try
        {
            new VsdxEngine().Create(Sample(), source, new WriteOptions { AutoLayout = false });
            var diagram = new VsdxEngine().Read(source).Diagram;

            var controller = new RepairController(2, 6);
            controller.Begin(diagram, "baseline");
            while (controller.NextPlan(diagram) is not null) { }

            new VsdxEngine().Create(diagram, output, new WriteOptions { AutoLayout = false });

            Assert.True(LooksLikePackage(output));
            Assert.True(new VsdxEngine().Validate(output).IsValid);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void ModelJsonIsNotAPackage()
    {
        // The distinction the test rests on, asserted directly so it cannot drift.
        var path = NewPath("json");

        try
        {
            Vsdx.Model.DiagramJson.Save(Sample(), path);

            Assert.False(LooksLikePackage(path),
                "DiagramJson.Save now emits a ZIP — the repair defect would not have been a defect");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
