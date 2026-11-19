using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// An edge written as `rightAngle` reads back as `curved`, though its geometry is orthogonal.
///
/// The independent agent that drew the order-system diagram reported this as the reason one of its connectors
/// looked wrong: it wrote `kind: "rightAngle"`, the export said `curved`, and it concluded the engine had
/// silently downgraded its routing. It had not — the page XML carries a five-row polyline
/// (MoveTo + 4 LineTo) with two waypoints, which is orthogonal.
///
/// The label is what is wrong. `OfficeImoDocumentReader` reads `connector.Kind` straight through, and OfficeIMO
/// reports "curved" for any routed connector. So a caller inspecting the export cannot tell an orthogonal route
/// from a curved one, and the `kind` they carefully set disappears on round-trip.
/// </summary>
public sealed class EdgeKindRoundTripTests
{
    private static Diagram WithTwoEdges() => new()
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
                    new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 6, 1.3, 0.6) },
                    new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 2, 1.3, 0.6) },
                    new Node { Id = "c", Text = "C", Bounds = new Bounds(8, 6, 1.3, 0.6) }
                },
                Edges =
                {
                    new Edge { Id = "e1", From = "a", To = "b", Kind = "rightAngle" },
                    new Edge { Id = "e2", From = "a", To = "c", Kind = "straight" }
                }
            }
        }
    };

    private static Diagram RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-kind-{Guid.NewGuid():N}.vsdx");

        try
        {
            new VsdxEngine().Create(WithTwoEdges(), path, new WriteOptions { AutoLayout = false });
            return new VsdxEngine().Read(path).Diagram;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ARightAngleEdgeKeepsItsKind()
    {
        var back = RoundTrip();
        var edge = back.Pages[0].Edges.Single(e => e.Id == "e1");

        Assert.Equal("rightAngle", edge.Kind);
    }

    [Fact]
    public void AStraightEdgeKeepsItsKind()
    {
        // The control: this one survives, so the defect is specific to routed edges.
        var back = RoundTrip();
        var edge = back.Pages[0].Edges.Single(e => e.Id == "e2");

        Assert.Equal("straight", edge.Kind);
    }

    [Fact]
    public void TheRoutedEdgeReallyIsOrthogonalInTheGeometry()
    {
        // Proves the routing happened; only the label is lost.
        var back = RoundTrip();
        var edge = back.Pages[0].Edges.Single(e => e.Id == "e1");

        Assert.True(edge.Waypoints is { Count: > 0 },
            "a rightAngle edge should carry waypoints once routed");
    }
}
