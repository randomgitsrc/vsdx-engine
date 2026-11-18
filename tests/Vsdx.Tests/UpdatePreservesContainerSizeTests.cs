using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Editing one node must not resize the containers around it.
///
/// `update` re-attaches members to their containers with `resizeToFit: group.AutoSize ?? true`, while
/// the writer uses `?? false`. Measured: exporting, changing one node's text and writing back grew
/// every container by 0.38" (1.230 -> 1.610) — a consequence of the node's own edit reaching shapes
/// the caller never touched.
///
/// This is the same divergence C7 fixed on the write path; only that half was changed at the time.
/// The charter requires that unmodified content survive an edit.
/// </summary>
public sealed class UpdatePreservesContainerSizeTests
{
    private static Diagram Banded()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 9 }
        };

        page.Groups.Add(new Group { Id = "band", Name = "Band", Bounds = new Bounds(6, 4, 8, 1.6) });
        page.Nodes.Add(new Node { Id = "a", Text = "A", GroupId = "band", Bounds = new Bounds(4, 4, 1.3, 0.55) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", GroupId = "band", Bounds = new Bounds(8, 4, 1.3, 0.55) });
        page.Edges.Add(new Edge { Id = "e", From = "a", To = "b" });

        return new Diagram { Id = "t", Pages = { page } };
    }

    [Fact]
    public void TextEditDoesNotChangeTheContainerHeight()
    {
        var diagram = Banded();
        var before = diagram.Pages[0].Groups[0].Bounds!.H;

        // Change only a node label — nothing about the container.
        diagram.Pages[0].Nodes[0].Text = "A changed";

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-upd-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = false });

        try
        {
            var written = new VsdxEngine().Read(path).Diagram;
            var after = written.Pages[0].Groups[0].Bounds!.H;

            Assert.Equal(before, after, 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnExplicitAutoSizeTrueStillResizes()
    {
        // The default must change, not the capability: a caller asking for auto-size still gets it.
        var diagram = Banded();
        diagram.Pages[0].Groups[0].AutoSize = true;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-upd2-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = false });

        try
        {
            var written = new VsdxEngine().Read(path).Diagram;
            Assert.NotNull(written.Pages[0].Groups[0].Bounds);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BothPathsAgreeOnTheDefault()
    {
        // Writer and incremental editor must not disagree about what an unset AutoSize means.
        var writer = File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Adapters", "OfficeImoDocumentWriter.cs"));
        var editor = File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Adapters", "OfficeImoIncrementalEditor.cs"));

        Assert.Contains("group.AutoSize ?? false", writer);
        Assert.Contains("group.AutoSize ?? false", editor);
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
