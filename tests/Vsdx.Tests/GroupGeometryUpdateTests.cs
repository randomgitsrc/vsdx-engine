using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Changing a container's geometry through `update` must either take effect or be reported.
///
/// `ApplyGroups` handled renaming and membership and never read `group.Bounds`. Measured: setting all
/// three containers to {1,1,7,3} produced a file byte-identical to the input, with `ok:true`,
/// `changes:[]` and `unmet:[]` — the case the project charter forbids by name: accepted by the
/// protocol, allowed by the schema, ignored in execution, and not reported.
///
/// `docs/limitations.md` and `docs/api.md` both state that groups can be modified, so silence here is
/// worse than a refusal.
/// </summary>
public sealed class GroupGeometryUpdateTests
{
    private static (Diagram Model, string Path) Base()
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

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-grp-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(new Diagram { Id = "t", Pages = { page } }, path,
            new WriteOptions { AutoLayout = false });

        return (new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void AChangedContainerSizeTakesEffect()
    {
        var (model, path) = Base();
        try
        {
            model.Pages[0].Groups[0].Bounds = new Bounds(5, 3, 4, 2);

            var output = Path.Combine(Path.GetTempPath(), $"vsdx-grp2-{Guid.NewGuid():N}.vsdx");
            new VsdxEngine().Update(path, model, output);

            try
            {
                var after = new VsdxEngine().Read(output).Diagram.Pages[0].Groups[0].Bounds!;

                Assert.Equal(4, after.W, 1);
                Assert.Equal(2, after.H, 1);
            }
            finally { File.Delete(output); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnIgnoredChangeWouldBeReported()
    {
        // The charter's rule: if it cannot be honoured, it must appear in the result. A silent no-op
        // is the one outcome that is never acceptable.
        var (model, path) = Base();
        try
        {
            model.Pages[0].Groups[0].Bounds = new Bounds(5, 3, 4, 2);

            var output = Path.Combine(Path.GetTempPath(), $"vsdx-grp3-{Guid.NewGuid():N}.vsdx");
            var result = new VsdxEngine().Update(path, model, output);

            try
            {
                var after = new VsdxEngine().Read(output).Diagram.Pages[0].Groups[0].Bounds!;
                bool changed = Math.Abs(after.W - 4) < 0.1 && Math.Abs(after.H - 2) < 0.1;
                bool reported = result.Changes.Any(c => c.Target == "band");

                Assert.True(changed || reported,
                    "container geometry change was neither applied nor reported");
            }
            finally { File.Delete(output); }
        }
        finally { File.Delete(path); }
    }
}
