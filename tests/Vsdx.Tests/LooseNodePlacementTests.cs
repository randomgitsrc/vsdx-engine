using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A node with no container can be laid out inside one, which tells the reader it belongs there.
///
/// Measured on `slack-cellular`: `users` declares no `containerId`, yet it renders at (4.05, 3.33) inside the
/// `edge` band spanning y[2.74, 4.26] — so a client appears to live inside "边缘层（跨区）". The source it was
/// transcribed from says the opposite: edge load balancers are in different regions from the user, and the legend
/// lists the client as its own kind.
///
/// The loose node shares a row with the band's own members, so the band grows around it. The other two shipped
/// cases do not have this: their loose nodes sit on rows of their own.
/// </summary>
public sealed class LooseNodePlacementTests
{
    private static Diagram LooseNodeBesideABand()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 14, Height = 9 } };

        page.Groups.Add(new Group { Id = "band", Name = "边缘层", Kind = "layer", Order = 2, Members = { "gw" } });
        page.Nodes.Add(new Node { Id = "gw", Text = "网关", GroupId = "band" });

        // No container: a peer of the band, not a member of it.
        page.Nodes.Add(new Node { Id = "loose", Text = "用户" });

        page.Edges.Add(new Edge { Id = "e", From = "loose", To = "gw" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Fact]
    public void ALooseNodeIsNotDrawnInsideAContainer()
    {
        var diagram = LooseNodeBesideABand();

        DiagramLayoutEngine.Apply(diagram,
            new LayoutOptions { Direction = LayoutDirection.TopToBottom }, LayoutStrategy.Layered);
        DiagramLayout.Apply(diagram);

        var page = diagram.Pages[0];
        var loose = page.Nodes.Single(n => n.Id == "loose").Bounds;

        foreach (var group in page.Groups.Where(g => g.Bounds is not null))
        {
            var b = group.Bounds!;
            bool inside = Math.Abs(loose.X - b.X) < b.W / 2 && Math.Abs(loose.Y - b.Y) < b.H / 2;

            Assert.False(inside,
                $"loose node at ({loose.X:F2},{loose.Y:F2}) sits inside container '{group.Id}'");
        }
    }
}
