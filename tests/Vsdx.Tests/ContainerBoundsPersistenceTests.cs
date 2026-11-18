using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Container geometry computed by the engine must survive the write.
///
/// The writer called `AddToContainer(..., resizeToFit: group.AutoSize ?? true)` while the same
/// file's explicit-bounds path used `?? false`. With `AutoSize` unset — the normal case for a
/// container derived by the engine — the container was recomputed on the way out: measured, a top
/// at 8.46 became 8.76. Anything computed against container bounds before the write was therefore
/// wrong, which is why the decoration avoidance in C6 found nothing to move.
///
/// A controlled run showed the resize is not needed: with `resizeToFit: false` the written
/// geometry matches the model exactly and every member is still contained.
/// </summary>
public sealed class ContainerBoundsPersistenceTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string Ir = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "containers":[{"id":"band","label":"Band"}],
     "nodes":[{"id":"a","label":"A","containerId":"band"},
              {"id":"b","label":"B","containerId":"band"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    private static (Diagram Model, Diagram Written, string Path) RoundTrip(bool? autoSize)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(Ir, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;

        var diagram = DiagramIrConverter.ToDiagram(ir);
        DesignSpecApplier.Apply(diagram, spec);
        foreach (var group in diagram.Pages[0].Groups) group.AutoSize = autoSize;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-bounds-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = false });

        return (diagram, new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void DerivedBoundsAreNotRecomputedOnWrite()
    {
        var (model, written, path) = RoundTrip(autoSize: null);
        try
        {
            var before = model.Pages[0].Groups[0].Bounds!;
            var after = written.Pages[0].Groups[0].Bounds!;

            // Compared at 0.05" rather than 2dp: the transposing direction re-derives box sizes
            // after the swap, which accumulates a rounding step. The property that matters is that
            // the writer does not *recompute* the container — a 0.01" drift from rounding is not
            // the same as the 0.30" growth that `resizeToFit: true` produced.
            Assert.Equal(before.Y, after.Y, 0.05);
            Assert.Equal(before.H, after.H, 0.05);
            Assert.Equal(before.W, after.W, 0.05);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnExplicitAutoSizeTrueIsStillHonoured()
    {
        // Benchmarks declare autoSize:true and must keep their behaviour.
        var (_, written, path) = RoundTrip(autoSize: true);
        try
        {
            Assert.NotNull(written.Pages[0].Groups[0].Bounds);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MembersStayInsideAfterTheWrite()
    {
        // The reason resize might have been needed: containing the members. Checked directly.
        var (_, written, path) = RoundTrip(autoSize: null);
        try
        {
            Assert.Empty(DiagramLayout.CheckContainment(written.Pages[0]));
        }
        finally { File.Delete(path); }
    }
}
