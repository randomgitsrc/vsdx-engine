using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Deleting a node that still has connectors must remove the connectors with it.
///
/// Measured: removing one node produced `ok:false` with `unhandled`, and the underlying cause was a
/// dangling reference — connector `e1` pointing at a shape that no longer existed, rejected by the
/// write-time validator. A cascade is what the caller asked for by passing `--delete-missing`.
///
/// A second defect in the same path: the change report listed a deletion that did not happen. After
/// removing an isolated node, `changes` contained `node.deleted '5'` — where `5` is a text block's
/// shape id, and the text block is still present in the output.
/// </summary>
public sealed class DeleteMissingTests
{
    private static Diagram Diagram_() => new()
    {
        Id = "t",
        Pages =
        {
            new Page
            {
                Name = "p",
                Size = new PageSize { Width = 10, Height = 8 },
                Nodes =
                {
                    new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 4, 1.4, 0.7) },
                    new Node { Id = "b", Text = "B", Bounds = new Bounds(7, 4, 1.4, 0.7) },
                    new Node { Id = "lone", Text = "L", Bounds = new Bounds(5, 2, 1.4, 0.7) }
                },
                Edges = { new Edge { Id = "e", From = "a", To = "b", Label = "x" } },
                TextBlocks =
                {
                    new TextBlock { Id = "title", Text = "T", Role = "title", Bounds = new Bounds(5, 7, 4, 0.5) }
                }
            }
        }
    };

    private static (string Source, Diagram Model) Base()
    {
        var source = Path.Combine(Path.GetTempPath(), $"vsdx-del-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(Diagram_(), source, new WriteOptions { AutoLayout = false });

        return (source, new VsdxEngine().Read(source).Diagram);
    }

    [Fact]
    public void DeletingANodeWithConnectorsSucceeds()
    {
        var (source, model) = Base();
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-del2-{Guid.NewGuid():N}.vsdx");

        try
        {
            model.Pages[0].Nodes.RemoveAll(n => n.Id == "a");
            model.Pages[0].Edges.RemoveAll(e => e.From == "a" || e.To == "a");

            var result = new VsdxEngine().Update(source, model, output,
                new UpdateOptions { DeleteMissing = true, AutoLayout = false });

            Assert.Empty(result.Compatibility.Errors);
            Assert.True(File.Exists(output));
        }
        finally { File.Delete(source); if (File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public void TheConnectorGoesWithItsNode()
    {
        var (source, model) = Base();
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-del3-{Guid.NewGuid():N}.vsdx");

        try
        {
            model.Pages[0].Nodes.RemoveAll(n => n.Id == "a");
            model.Pages[0].Edges.RemoveAll(e => e.From == "a" || e.To == "a");

            new VsdxEngine().Update(source, model, output,
                new UpdateOptions { DeleteMissing = true, AutoLayout = false });

            var written = new VsdxEngine().Read(output).Diagram.Pages[0];

            Assert.DoesNotContain(written.Nodes, n => n.Id == "a");
            Assert.DoesNotContain(written.Edges, e => e.From == "a" || e.To == "a");
        }
        finally { File.Delete(source); if (File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public void TheReportNamesOnlyWhatWasDeleted()
    {
        // The defect: a text block's shape id appeared as a deleted node, and the block survived.
        var (source, model) = Base();
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-del4-{Guid.NewGuid():N}.vsdx");

        try
        {
            model.Pages[0].Nodes.RemoveAll(n => n.Id == "lone");

            var result = new VsdxEngine().Update(source, model, output,
                new UpdateOptions { DeleteMissing = true, AutoLayout = false });

            var deletions = result.Changes.Where(c => c.Kind == "node.deleted").Select(c => c.Target).ToList();

            Assert.Contains("lone", deletions);
            // Whatever else is reported must actually be gone from the model.
            foreach (var target in deletions.Where(t => t != "lone"))
                Assert.DoesNotContain(model.Pages[0].Nodes, n => n.Id == target);
        }
        finally { File.Delete(source); if (File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public void TextBlocksSurviveADeletionElsewhere()
    {
        var (source, model) = Base();
        var output = Path.Combine(Path.GetTempPath(), $"vsdx-del5-{Guid.NewGuid():N}.vsdx");

        try
        {
            model.Pages[0].Nodes.RemoveAll(n => n.Id == "lone");
            new VsdxEngine().Update(source, model, output,
                new UpdateOptions { DeleteMissing = true, AutoLayout = false });

            var written = new VsdxEngine().Read(output).Diagram.Pages[0];

            Assert.Contains(written.TextBlocks, t => t.Id == "title");
        }
        finally { File.Delete(source); if (File.Exists(output)) File.Delete(output); }
    }
}
