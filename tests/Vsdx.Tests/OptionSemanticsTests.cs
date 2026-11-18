using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// An option must control what its name says, and nothing else.
///
/// `OfficeImoIncrementalEditor` calls `doc.RequestRecalcOnOpen()` under
/// `if (options.ValidateAfterSave)` — a validation flag driving a recalculation request. The two are
/// unrelated: one asks the backend to check the result, the other asks Visio to recompute on open.
/// `UpdateOptions` has no `RequestRecalc` field, so the nearest boolean was borrowed.
///
/// The consequence is that `--no-validate` silently also disables recalculation, which a caller has no
/// reason to expect.
/// </summary>
public sealed class OptionSemanticsTests
{
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
    public void ValidationDoesNotControlRecalculation()
    {
        // The two options must be independent: turning validation off may not silently stop the
        // writer from asking Visio to recalculate on open.
        var source = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Adapters", "OfficeImoIncrementalEditor.cs"));

        Assert.DoesNotContain("if (options.ValidateAfterSave) doc.RequestRecalcOnOpen();", source);
    }

    [Fact]
    public void UpdateHasItsOwnRecalculationOption()
    {
        // If recalculation is a real choice, it needs a field of its own rather than borrowing one.
        var options = typeof(UpdateOptions).GetProperty("RequestRecalc");

        Assert.NotNull(options);
    }

    [Fact]
    public void DisablingValidationStillWritesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-opt-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(Sample(), path, new WriteOptions { AutoLayout = false, ValidateAfterSave = false });

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void EveryWriteOptionChangesSomething()
    {
        // Each flag must have an observable effect, or it is a promise with no content.
        var baseline = Path.Combine(Path.GetTempPath(), $"vsdx-opt2-{Guid.NewGuid():N}.vsdx");
        var fitted = Path.Combine(Path.GetTempPath(), $"vsdx-opt3-{Guid.NewGuid():N}.vsdx");

        try
        {
            var model = Sample();
            model.Pages[0].Nodes[0].Text = "这是一个明显更长的节点名称用来触发文字推导";

            new VsdxEngine().Create(model, baseline, new WriteOptions { AutoLayout = false });
            new VsdxEngine().Create(model, fitted, new WriteOptions { AutoLayout = false, FitToText = true });

            double plain = new VsdxEngine().Read(baseline).Diagram.Pages[0].Nodes[0].Bounds!.W;
            double wide = new VsdxEngine().Read(fitted).Diagram.Pages[0].Nodes[0].Bounds!.W;

            Assert.True(wide > plain, $"FitToText changed nothing: {plain:F2} vs {wide:F2}");
        }
        finally
        {
            if (File.Exists(baseline)) File.Delete(baseline);
            if (File.Exists(fitted)) File.Delete(fitted);
        }
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
